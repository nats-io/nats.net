namespace NATS.Client.Core;

public readonly record struct NatsSubOpts
{
    /// <summary>
    /// If specified, the subscriber will join this queue group.
    /// </summary>
    /// <remarks>
    /// Subscribers with the same queue group name, become a queue group,
    /// and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </remarks>
    public string? QueueGroup { get; init; }

    /// <summary>
    /// Serializer to use to deserialize the message if a model is being used.
    /// </summary>
    /// <remarks>
    /// If not set, serializer set in connection options or the default JSON serializer
    /// will be used.
    /// </remarks>
    public INatsSerializer? Serializer { get; init; }

    /// <summary>
    /// Number of messages to wait for before automatically unsubscribing.
    /// </summary>
    /// <remarks>
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </remarks>
    public int? MaxMsgs { get; init; }

    /// <summary>
    /// Amount of time to wait before automatically unsubscribing.
    /// </summary>
    /// <remarks>
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </remarks>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Maximum amount of time allowed before the first message is received.
    /// If exceeded, subscription will be automatically unsubscribed.
    /// </summary>
    /// <remarks>
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </remarks>
    public TimeSpan? StartUpTimeout { get; init; }

    /// <summary>
    /// Maximum amount of time allowed between any two subsequent messages
    /// before automatically unsubscribing.
    /// </summary>
    /// <remarks>
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </remarks>
    public TimeSpan? IdleTimeout { get; init; }
}
