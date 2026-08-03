using System.Runtime.CompilerServices;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// Represents a NATS JetStream push consumer.
/// </summary>
/// <remarks>
/// <para>
/// A push consumer subscribes to the consumer's deliver subject and receives the messages
/// as they are pushed by the JetStream server. Whether a message requires an acknowledgment
/// depends on the consumer's <see cref="ConsumerConfigAckPolicy"/>, see
/// <see cref="NatsJSPushConsumerOpts.AckPolicy"/> for details.
/// </para>
/// <para>
/// The server manages the message delivery for the consumer, so pull based operations
/// (<see cref="NextAsync{T}"/>, <see cref="FetchAsync{T}"/> and
/// <see cref="FetchNoWaitAsync{T}"/>) are not supported and throw a
/// <see cref="NatsJSProtocolException"/>.
/// </para>
/// </remarks>
public class NatsJSPushConsumer : INatsJSPushConsumer
{
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private volatile bool _deleted;

    internal NatsJSPushConsumer(NatsJSContext context, ConsumerInfo info)
    {
        _context = context;
        Info = info;
        _stream = Info.StreamName;
        _consumer = Info.Name;
    }

    /// <inheritdoc />
    public ConsumerInfo Info { get; private set; }

    /// <summary>
    /// Delete this consumer.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>After deletion this object can't be used anymore.</remarks>
    public async ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _deleted = await _context.DeleteConsumerAsync(_stream, _consumer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts an enumerator consuming messages from this push consumer.
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Consume options for the underlying subscription.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Async enumerable of messages which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">This consumer object isn't valid anymore because it was deleted earlier or the consumer has no deliver subject.</exception>
    public async IAsyncEnumerable<INatsJSMsg<T>> ConsumeAsync<T>(
        INatsDeserialize<T>? serializer = default,
        NatsJSConsumeOpts? opts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        cancellationToken.ThrowIfCancellationRequested();

        var deliverSubject = Info.Config.DeliverSubject;
        if (deliverSubject is not { Length: > 0 })
        {
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' doesn't have a deliver subject so it can't be consumed as a push consumer.");
        }

        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();

        var subOpts = new NatsSubOpts
        {
            ChannelOpts = new NatsSubChannelOpts
            {
                // Keep capacity large enough not to block the socket reads.
                // Uses connection's default FullMode (typically DropNewest) to avoid
                // blocking the main NATS TCP connection on slow consumers.
                Capacity = 1_000,
            },
        };

        var sub = new NatsJSPushConsume<T>(
            context: _context,
            subject: deliverSubject,
            queueGroup: Info.Config.DeliverGroup,
            idleHeartbeat: Info.Config.IdleHeartbeat == TimeSpan.Zero ? TimeSpan.FromSeconds(5) : Info.Config.IdleHeartbeat,
            notificationHandler: opts?.NotificationHandler,
            serializer: serializer,
            opts: subOpts,
            cancellationToken: cancellationToken);

        await _context.Connection.AddSubAsync(sub: sub, cancellationToken).ConfigureAwait(false);

        await using (sub)
        {
            sub.MarkReaderActive();
            try
            {
                while (true)
                {
                    // We have to check calls individually since we can't use yield return in try-catch blocks.
                    try
                    {
                        var ready = await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                        if (!ready)
                            yield break;
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }

                    while (true)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            yield break;

                        bool read;
                        NatsJSMsg<T> jsMsg;
                        try
                        {
                            read = sub.Msgs.TryRead(out jsMsg);
                        }
                        catch (OperationCanceledException)
                        {
                            read = false;
                            jsMsg = default;
                        }

                        if (!read)
                            break;

                        // if yield is blocked by the application code, we don't want
                        // heartbeat timer kicking in and issuing a spurious flow control.
                        sub.StopHeartbeatTimer();
                        yield return jsMsg;
                        sub.ResetHeartbeatTimer();
                    }
                }
            }
            finally
            {
                sub.MarkReaderInactive();
            }
        }
    }

    /// <summary>
    /// Consume a single message using a pull subscription on this consumer.
    /// </summary>
    /// <remarks>Push consumers don't support this operation.</remarks>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <exception cref="NatsJSProtocolException">Consumer is push based.</exception>
    public ValueTask<INatsJSMsg<T>?> NextAsync<T>(
        INatsDeserialize<T>? serializer = default,
        NatsJSNextOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        throw NatsJSProtocolException.ConsumerIsPushBased();
    }

    /// <summary>
    /// Consume messages using a pull subscription on this consumer.
    /// </summary>
    /// <remarks>Push consumers don't support this operation.</remarks>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <exception cref="NatsJSProtocolException">Consumer is push based.</exception>
    public IAsyncEnumerable<INatsJSMsg<T>> FetchAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        throw NatsJSProtocolException.ConsumerIsPushBased();
    }

    /// <summary>
    /// Consume a set number of messages using a pull subscription on this consumer without waiting.
    /// </summary>
    /// <remarks>Push consumers don't support this operation.</remarks>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <exception cref="NatsJSProtocolException">Consumer is push based.</exception>
    public IAsyncEnumerable<INatsJSMsg<T>> FetchNoWaitAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        throw NatsJSProtocolException.ConsumerIsPushBased();
    }

    /// <inheritdoc />
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default) =>
        Info = await _context.JSRequestResponseAsync<object, ConsumerInfo>(
            subject: $"{_context.Opts.Prefix}.CONSUMER.INFO.{_stream}.{_consumer}",
            request: null,
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask UnpinAsync(string group, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        throw NatsJSProtocolException.ConsumerIsPushBased();
    }

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }
}
