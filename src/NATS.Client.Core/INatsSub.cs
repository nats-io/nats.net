using System.Threading.Channels;

namespace NATS.Client.Core;

public interface INatsSub : IAsyncDisposable
{
    /// <summary>
    /// Access incoming messages for your subscription.
    /// </summary>
    ChannelReader<NatsMsg> Msgs { get; }

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
}

public interface INatsSub<T> : IAsyncDisposable
{
    /// <summary>
    /// Access incoming messages for your subscription.
    /// </summary>
    ChannelReader<NatsMsg<T?>> Msgs { get; }

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
}
