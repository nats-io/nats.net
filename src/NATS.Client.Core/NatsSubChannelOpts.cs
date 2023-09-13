using System.Threading.Channels;

namespace NATS.Client.Core;

/// <summary>
/// Options For setting the FullMode and Capacity for a the <see cref="Channel"/> created for Subscriptions
/// </summary>
public record NatsSubChannelOpts
{
    /// <summary>
    /// The Behavior of the Subscription's Channel when the Capacity has been reached.
    /// By default, the behavior is <seealso cref="BoundedChannelFullMode.Wait"/>
    /// </summary>
    public BoundedChannelFullMode? FullMode { get; init; }

    /// <summary>
    /// The Maximum Capacity for the channel. If not specified, a default of 1000 is used.
    /// </summary>
    public int? Capacity { get; init; }
}
