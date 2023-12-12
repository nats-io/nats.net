using System.Threading.Channels;

namespace NATS.Client.Core;

public record NatsSubOpts
{
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

    /// <summary>
    /// If true, end the subscription upon receiving an empty message.
    /// The empty message will not be delivered to the subscription.
    /// </summary>
    /// <remarks>
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </remarks>
    public bool? StopOnEmptyMsg { get; init; }

    /// <summary>
    /// If true, end the subscription and throw an exception if a
    /// no responders status message is received.
    /// The no responders status message will not be delivered to the subscription.
    /// </summary>
    /// <remarks>
    /// If not set, all published messages will be received until explicitly
    /// unsubscribed or disposed.
    /// </remarks>
    public bool? ThrowIfNoResponders { get; init; }

    /// <summary>
    /// Allows Configuration of <see cref="Channel"/> options for a subscription.
    /// </summary>
    public NatsSubChannelOpts? ChannelOpts { get; init; }
}
