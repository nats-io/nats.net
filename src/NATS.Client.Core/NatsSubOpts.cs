namespace NATS.Client.Core;

public readonly record struct NatsSubOpts
{
    /// <summary>
    /// If specified, the subscriber will join this queue group. Subscribers with the same queue group name,
    /// become a queue group, and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </summary>
    public string? QueueGroup { get; init; }

    /// <summary>
    /// Serializer to use to deserialize the message if a model is being used.
    /// If not set, serializer set in connection options or the default JSON serializer
    /// will be used.
    /// </summary>
    public INatsSerializer? Serializer { get; init; }

    /// <summary>
    /// Number of messages to wait for before automatically unsubscribing.
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </summary>
    public int? MaxMsgs { get; init; }

    /// <summary>
    /// Amount of time to wait before automatically unsubscribing.
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Maximum amount of time allowed before the first message is received.
    /// If exceeded, subscription will be automatically unsubscribed.
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </summary>
    public TimeSpan? StartUpTimeout { get; init; }

    /// <summary>
    /// Maximum amount of time allowed between any two subsequent messages
    /// before automatically unsubscribing.
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </summary>
    public TimeSpan? IdleTimeout { get; init; }
}
