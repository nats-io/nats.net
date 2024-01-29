using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// Represents a NATS JetStream consumer.
/// </summary>
public class NatsJSConsumer : INatsJSConsumer
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
        return _deleted = await _context.DeleteConsumerAsync(_stream, _consumer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts an enumerator consuming messages from the stream using this consumer.
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Consume options. (default: <c>MaxMsgs</c> 1,000)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Async enumerable of messages which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    public async IAsyncEnumerable<NatsJSMsg<T>> ConsumeAsync<T>(
        INatsDeserialize<T>? serializer = default,
        NatsJSConsumeOpts? opts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        opts ??= _context.Opts.DefaultConsumeOpts;
        await using var cc = await ConsumeInternalAsync<T>(serializer, opts, cancellationToken).ConfigureAwait(false);

        // Keep subscription alive (since it's a weak ref in subscription manager) until we're done.
        using var anchor = _context.Connection.RegisterSubAnchor(cc);

        while (!cancellationToken.IsCancellationRequested)
        {
            // We have to check calls individually since we can't use yield return in try-catch blocks.
            bool ready;
            try
            {
                ready = await cc.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ready = false;
            }

            if (!ready)
                yield break;

            while (!cancellationToken.IsCancellationRequested)
            {
                bool read;
                NatsJSMsg<T> jsMsg;
                try
                {
                    read = cc.Msgs.TryRead(out jsMsg);
                }
                catch (OperationCanceledException)
                {
                    read = false;
                    jsMsg = default;
                }

                if (!read)
                    break;

                yield return jsMsg;
            }
        }
    }

    /// <summary>
    /// Consume a single message from the stream using this consumer.
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
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
    public async ValueTask<NatsJSMsg<T>?> NextAsync<T>(INatsDeserialize<T>? serializer = default, NatsJSNextOpts? opts = default, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        opts ??= _context.Opts.DefaultNextOpts;
        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();

        await using var f = await FetchInternalAsync<T>(
            new NatsJSFetchOpts
            {
                MaxMsgs = 1,
                IdleHeartbeat = opts.IdleHeartbeat,
                Expires = opts.Expires,
                NotificationHandler = opts.NotificationHandler,
            },
            serializer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Keep subscription alive (since it's a weak ref in subscription manager) until we're done.
        using var anchor = _context.Connection.RegisterSubAnchor(f);

        await foreach (var natsJSMsg in f.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            return natsJSMsg;
        }

        return null;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NatsJSMsg<T>> FetchAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();

        await using var fc = await FetchInternalAsync<T>(opts, serializer, cancellationToken).ConfigureAwait(false);

        // Keep subscription alive (since it's a weak ref in subscription manager) until we're done.
        using var anchor = _context.Connection.RegisterSubAnchor(fc);

        while (!cancellationToken.IsCancellationRequested)
        {
            // We have to check calls individually since we can't use yield return in try-catch blocks.
            bool ready;
            try
            {
                ready = await fc.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ready = false;
            }

            if (!ready)
                yield break;

            while (!cancellationToken.IsCancellationRequested)
            {
                bool read;
                NatsJSMsg<T> jsMsg;
                try
                {
                    read = fc.Msgs.TryRead(out jsMsg);
                }
                catch (OperationCanceledException)
                {
                    read = false;
                    jsMsg = default;
                }

                if (!read)
                    break;

                yield return jsMsg;
            }
        }
    }

    /// <summary>
    /// Consume a set number of messages from the stream using this consumer.
    /// Returns immediately if no messages are available.
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Fetch options. (default: <c>MaxMsgs</c> 1,000 and timeout is ignored)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Async enumerable of messages which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <remarks>
    /// <para>
    /// This method will return immediately if no messages are available.
    /// </para>
    /// <para>
    /// Using this method is discouraged because it might create an unnecessary load on your cluster.
    /// Use <c>Consume</c> or <c>Fetch</c> instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>
    /// However, there are scenarios where this method is useful. For example if your application is
    /// processing messages in batches infrequently (for example every 5 minutes) you might want to
    /// consider <c>FetchNoWait</c>. You must make sure to count your messages and stop fetching
    /// if you received all of them in one call, meaning when <c>count &lt; MaxMsgs</c>.
    /// </para>
    /// <code>
    /// const int max = 10;
    /// var count = 0;
    ///
    /// await foreach (var msg in consumer.FetchAllNoWaitAsync&lt;int&gt;(new NatsJSFetchOpts { MaxMsgs = max }))
    /// {
    ///     count++;
    ///     Process(msg);
    ///     await msg.AckAsync();
    /// }
    ///
    /// if (count &lt; max)
    /// {
    ///     // No more messages. Pause for more.
    ///     await Task.Delay(TimeSpan.FromMinutes(5));
    /// }
    /// </code>
    /// </example>
    public async IAsyncEnumerable<NatsJSMsg<T>> FetchNoWaitAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();

        await using var fc = await FetchInternalAsync<T>(opts with { NoWait = true }, serializer, cancellationToken).ConfigureAwait(false);
        await foreach (var jsMsg in fc.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return jsMsg;
        }
    }

    /// <summary>
    /// Retrieve the consumer info from the server and update this consumer.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default) =>
        Info = await _context.JSRequestResponseAsync<object, ConsumerInfo>(
            Telemetry.NatsActivities,
            subject: $"{_context.Opts.Prefix}.CONSUMER.INFO.{_stream}.{_consumer}",
            request: null,
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask<NatsJSConsume<T>> ConsumeInternalAsync<T>(INatsDeserialize<T>? serializer = default, NatsJSConsumeOpts? opts = default, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        opts ??= new NatsJSConsumeOpts();
        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();
        var inbox = _context.NewInbox();

        var max = NatsJSOptsDefaults.SetMax(opts.MaxMsgs, opts.MaxBytes, opts.ThresholdMsgs, opts.ThresholdBytes);
        var timeouts = NatsJSOptsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);

        var requestOpts = BuildRequestOpts(opts.MaxMsgs);

        var sub = new NatsJSConsume<T>(
            stream: _stream,
            consumer: _consumer,
            context: _context,
            subject: inbox,
            queueGroup: default,
            serializer: serializer,
            opts: requestOpts,
            maxMsgs: max.MaxMsgs,
            maxBytes: max.MaxBytes,
            thresholdMsgs: max.ThresholdMsgs,
            thresholdBytes: max.ThresholdBytes,
            expires: timeouts.Expires,
            idle: timeouts.IdleHeartbeat,
            notificationHandler: opts.NotificationHandler,
            cancellationToken: cancellationToken);

        await _context.Connection.SubAsync(sub: sub, cancellationToken).ConfigureAwait(false);

        // Start consuming with the first Pull Request
        await sub.CallMsgNextAsync(
            "init",
            new ConsumerGetnextRequest
            {
                Batch = max.MaxMsgs,
                MaxBytes = max.MaxBytes,
                IdleHeartbeat = timeouts.IdleHeartbeat,
                Expires = timeouts.Expires,
            },
            cancellationToken).ConfigureAwait(false);

        sub.ResetHeartbeatTimer();

        return sub;
    }

    internal async ValueTask<NatsJSOrderedConsume<T>> OrderedConsumeInternalAsync<T>(INatsDeserialize<T>? serializer, NatsJSConsumeOpts opts, CancellationToken cancellationToken)
    {
        ThrowIfDeleted();

        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();
        var inbox = _context.NewInbox();

        var max = NatsJSOptsDefaults.SetMax(opts.MaxMsgs, opts.MaxBytes, opts.ThresholdMsgs, opts.ThresholdBytes);
        var timeouts = NatsJSOptsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);

        var requestOpts = BuildRequestOpts(opts.MaxMsgs);

        var sub = new NatsJSOrderedConsume<T>(
            stream: _stream,
            consumer: _consumer,
            context: _context,
            subject: inbox,
            queueGroup: default,
            serializer: serializer,
            opts: requestOpts,
            maxMsgs: max.MaxMsgs,
            maxBytes: max.MaxBytes,
            thresholdMsgs: max.ThresholdMsgs,
            thresholdBytes: max.ThresholdBytes,
            expires: timeouts.Expires,
            idle: timeouts.IdleHeartbeat,
            cancellationToken: cancellationToken);

        await _context.Connection.SubAsync(sub: sub, cancellationToken).ConfigureAwait(false);

        // Start consuming with the first Pull Request
        await sub.CallMsgNextAsync(
            "init",
            new ConsumerGetnextRequest
            {
                Batch = max.MaxMsgs,
                MaxBytes = max.MaxBytes,
                IdleHeartbeat = timeouts.IdleHeartbeat,
                Expires = timeouts.Expires,
            },
            cancellationToken).ConfigureAwait(false);

        sub.ResetHeartbeatTimer();

        return sub;
    }

    internal async ValueTask<NatsJSFetch<T>> FetchInternalAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();

        var inbox = _context.NewInbox();

        var max = NatsJSOptsDefaults.SetMax(opts.MaxMsgs, opts.MaxBytes);
        var timeouts = NatsJSOptsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);

        var requestOpts = BuildRequestOpts(opts.MaxMsgs);

        var sub = new NatsJSFetch<T>(
            stream: _stream,
            consumer: _consumer,
            context: _context,
            subject: inbox,
            queueGroup: default,
            serializer: serializer,
            opts: requestOpts,
            maxMsgs: max.MaxMsgs,
            maxBytes: max.MaxBytes,
            expires: timeouts.Expires,
            notificationHandler: opts.NotificationHandler,
            idle: timeouts.IdleHeartbeat,
            cancellationToken: cancellationToken);

        await _context.Connection.SubAsync(sub: sub, cancellationToken).ConfigureAwait(false);

        await sub.CallMsgNextAsync(
            opts.NoWait

                // When no wait is set we don't need to send the idle heartbeat and expiration
                // If no message is available the server will respond with a 404 immediately
                // If messages are available the server will send a 408 direct after the last message
                ? new ConsumerGetnextRequest { Batch = max.MaxMsgs, MaxBytes = max.MaxBytes, NoWait = opts.NoWait }
                : new ConsumerGetnextRequest
                {
                    Batch = max.MaxMsgs,
                    MaxBytes = max.MaxBytes,
                    IdleHeartbeat = timeouts.IdleHeartbeat,
                    Expires = timeouts.Expires,
                    NoWait = opts.NoWait,
                },
            cancellationToken).ConfigureAwait(false);

        sub.ResetHeartbeatTimer();

        return sub;
    }

    private static NatsSubOpts BuildRequestOpts(int? maxMsgs) =>
        new()
        {
            ChannelOpts = new NatsSubChannelOpts
            {
                // Keep capacity large enough not to block the socket reads.
                // This might delay message acknowledgements on slow consumers
                // but it's crucial to keep the reads flowing on the main
                // NATS TCP connection.
                Capacity = maxMsgs > 0 ? maxMsgs * 2 : 1_000,
                FullMode = BoundedChannelFullMode.Wait,
            },
        };

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }
}
