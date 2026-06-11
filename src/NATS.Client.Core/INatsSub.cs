using System.Threading.Channels;

namespace NATS.Client.Core;

public interface INatsSub<T> : IAsyncDisposable
{
    /// <summary>
    /// Access incoming messages for your subscription.
    /// </summary>
    ChannelReader<NatsMsg<T>> Msgs { get; }

    /// <summary>
    /// The subject name to subscribe to.
    /// </summary>
    string Subject { get; }

    /// <summary>
    /// If specified, the subscriber will join this queue group. Subscribers with the same queue group name,
    /// become a queue group, and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </summary>
    string? QueueGroup { get; }

    /// <summary>
    /// Complete the message channel, stop timers if they were used and send an unsubscribe
    /// message to the server.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous server UNSUB operation.</returns>
    public ValueTask UnsubscribeAsync();

    /// <summary>
    /// Drain the subscription: stop receiving new messages while letting messages
    /// already buffered be read, keeping the connection usable.
    /// </summary>
    /// <remarks>
    /// Sends an unsubscribe to the server, waits for a PING/PONG round-trip so messages
    /// already in flight are delivered, then completes <see cref="Msgs"/>. Keep reading
    /// <see cref="Msgs"/> until it completes to process the buffered messages. Unlike
    /// <see cref="UnsubscribeAsync"/>, drain does not drop messages still in the socket
    /// buffer. The connection stays open.
    /// </remarks>
    /// <param name="cancellationToken">Bounds the best-effort PING/PONG fence wait, which lasts at most <see cref="NatsOpts.DrainPingTimeout"/>; the token can only shorten it, not extend it past that timeout.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous drain operation.</returns>
    public ValueTask DrainAsync(CancellationToken cancellationToken = default);
}
