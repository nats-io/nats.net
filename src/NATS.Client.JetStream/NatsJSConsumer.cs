using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// Represents a NATS JetStream consumer.
/// </summary>
public class NatsJSConsumer
{
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private volatile bool _deleted;

    internal NatsJSConsumer(NatsJSContext context, ConsumerInfo info)
    {
        _context = context;
        Info = info;
        _stream = Info.StreamName;
        _consumer = Info.Name;
    }

    /// <summary>
    /// Consumer info object as retrieved from NATS JetStream server at the time this object was created, updated or refreshed.
    /// </summary>
    public ConsumerInfo Info { get; private set; }

    /// <summary>
    /// Delete this consumer.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>After deletion this object can't be used anymore.</remarks>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    public async ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _deleted = await _context.DeleteConsumerAsync(_stream, _consumer, cancellationToken);
    }

    /// <summary>
    /// Starts an enumerator consuming messages from the stream using this consumer.
    /// </summary>
    /// <param name="opts">Consume options. (default: <c>MaxMsgs</c> 1,000)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Async enumerable of messages which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    public async IAsyncEnumerable<NatsJSMsg<T?>> ConsumeAllAsync<T>(
        NatsJSConsumeOpts? opts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        opts ??= _context.Opts.DefaultConsumeOpts;
        await using var cc = await ConsumeAsync<T>(opts, cancellationToken);
        await foreach (var jsMsg in cc.Msgs.ReadAllAsync(cancellationToken))
        {
            yield return jsMsg;
        }
    }

    /// <summary>
    /// Starts consuming messages from the stream using this consumer.
    /// </summary>
    /// <param name="opts">Consume options. (default: <c>MaxMsgs</c> 1,000)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>A consume object to manage the operation and retrieve messages.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    public async ValueTask<INatsJSConsume<T>> ConsumeAsync<T>(NatsJSConsumeOpts? opts = default, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        var inbox = _context.NewInbox();

        var max = NatsJSOptsDefaults.SetMax(opts.MaxMsgs, opts.MaxBytes, opts.ThresholdMsgs, opts.ThresholdBytes);
        var timeouts = NatsJSOptsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);

        var requestOpts = new NatsSubOpts
        {
            Serializer = opts.Serializer,
            ChannelOpts = new NatsSubChannelOpts
            {
                // Keep capacity at 1 to make sure message acknowledgements are sent
                // right after the message is processed and messages aren't queued up
                // which might cause timeouts for acknowledgments.
                Capacity = 1,
                FullMode = BoundedChannelFullMode.Wait,
            },
        };

        var sub = new NatsJSConsume<T>(
            stream: _stream,
            consumer: _consumer,
            context: _context,
            subject: inbox,
            queueGroup: default,
            opts: requestOpts,
            maxMsgs: max.MaxMsgs,
            maxBytes: max.MaxBytes,
            thresholdMsgs: max.ThresholdMsgs,
            thresholdBytes: max.ThresholdBytes,
            expires: timeouts.Expires,
            idle: timeouts.IdleHeartbeat);

        await _context.Connection.SubAsync(
            subject: inbox,
            queueGroup: default,
            opts: requestOpts,
            sub: sub,
            cancellationToken);

        await sub.CallMsgNextAsync(
            new ConsumerGetnextRequest
            {
                Batch = max.MaxMsgs,
                MaxBytes = max.MaxBytes,
                IdleHeartbeat = timeouts.IdleHeartbeat.ToNanos(),
                Expires = timeouts.Expires.ToNanos(),
            },
            cancellationToken);

        sub.ResetPending();
        sub.ResetHeartbeatTimer();

        return sub;
    }

    /// <summary>
    /// Consume a single message from the stream using this consumer.
    /// </summary>
    /// <param name="opts">Next message options. (default: 30 seconds timeout)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Message retrieved from the stream or <c>NULL</c></returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <remarks>
    /// <para>
    /// If the request to server expires (in 30 seconds by default) this call returns <c>NULL</c>.
    /// </para>
    /// <para>
    /// This method is implemented as a fetch with <c>MaxMsgs=1</c> which means every request will create a new subscription
    /// on the NATS server. This would be inefficient if you're consuming a lot of messages and you should consider using
    /// fetch or consume methods.
    /// </para>
    /// </remarks>
    /// <example>
    /// The following example shows how you might process messages:
    /// <code lang="C#">
    /// var next = await consumer.NextAsync&lt;Data&gt;();
    /// if (next is { } msg)
    /// {
    ///     // process the message
    ///     await msg.AckAsync();
    /// }
    /// </code>
    /// </example>
    public async ValueTask<NatsJSMsg<T?>?> NextAsync<T>(NatsJSNextOpts? opts = default, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        opts ??= _context.Opts.DefaultNextOpts;

        await using var f = await FetchAsync<T>(
            new NatsJSFetchOpts
            {
                MaxMsgs = 1,
                IdleHeartbeat = opts.IdleHeartbeat,
                Expires = opts.Expires,
                Serializer = opts.Serializer,
            },
            cancellationToken: cancellationToken);

        await foreach (var natsJSMsg in f.Msgs.ReadAllAsync(cancellationToken))
        {
            return natsJSMsg;
        }

        return default;
    }

    /// <summary>
    /// Consume a set number of messages from the stream using this consumer.
    /// </summary>
    /// <param name="opts">Fetch options. (default: <c>MaxMsgs</c> 1,000 and timeout in 30 seconds)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Async enumerable of messages which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    public async IAsyncEnumerable<NatsJSMsg<T?>> FetchAllAsync<T>(
        NatsJSFetchOpts? opts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        opts ??= _context.Opts.DefaultFetchOpts;

        await using var fc = await FetchAsync<T>(opts, cancellationToken);
        await foreach (var jsMsg in fc.Msgs.ReadAllAsync(cancellationToken))
        {
            yield return jsMsg;
        }
    }

    /// <summary>
    /// Consume a set number of messages from the stream using this consumer.
    /// </summary>
    /// <param name="opts">Fetch options. (default: <c>MaxMsgs</c> 1,000 and timeout in 30 seconds)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>A fetch object to manage the operation and retrieve messages.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    public async ValueTask<INatsJSFetch<T>> FetchAsync<T>(
        NatsJSFetchOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        opts ??= _context.Opts.DefaultFetchOpts;

        var inbox = _context.NewInbox();

        var max = NatsJSOptsDefaults.SetMax(opts.MaxMsgs, opts.MaxBytes);
        var timeouts = NatsJSOptsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);

        var requestOpts = new NatsSubOpts
        {
            Serializer = opts.Serializer,
            ChannelOpts = new NatsSubChannelOpts
            {
                // Keep capacity at 1 to make sure message acknowledgements are sent
                // right after the message is processed and messages aren't queued up
                // which might cause timeouts for acknowledgments.
                Capacity = 1,
                FullMode = BoundedChannelFullMode.Wait,
            },
        };

        var sub = new NatsJSFetch<T>(
            stream: _stream,
            consumer: _consumer,
            context: _context,
            subject: inbox,
            queueGroup: default,
            opts: requestOpts,
            maxMsgs: max.MaxMsgs,
            maxBytes: max.MaxBytes,
            expires: timeouts.Expires,
            idle: timeouts.IdleHeartbeat);

        await _context.Connection.SubAsync(
            subject: inbox,
            queueGroup: default,
            opts: requestOpts,
            sub: sub,
            cancellationToken);

        await sub.CallMsgNextAsync(
            new ConsumerGetnextRequest
            {
                Batch = max.MaxMsgs,
                MaxBytes = max.MaxBytes,
                IdleHeartbeat = timeouts.IdleHeartbeat.ToNanos(),
                Expires = timeouts.Expires.ToNanos(),
            },
            cancellationToken);

        sub.ResetHeartbeatTimer();

        return sub;
    }

    /// <summary>
    /// Retrieve the consumer info from the server and update this consumer.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default) =>
        Info = await _context.JSRequestResponseAsync<object, ConsumerInfo>(
            subject: $"{_context.Opts.Prefix}.CONSUMER.INFO.{_stream}.{_consumer}",
            request: null,
            cancellationToken).ConfigureAwait(false);

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }
}
