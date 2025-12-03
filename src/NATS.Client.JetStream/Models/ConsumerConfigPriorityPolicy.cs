namespace NATS.Client.JetStream.Models;

/// <summary>
/// The priority policy for consumer message selection.
/// </summary>
public enum ConsumerConfigPriorityPolicy
{
    /// <summary>
    /// No priority policy is set.
    /// </summary>
    None = 0,

    /// <summary>
    /// Messages are delivered based on priority level.
    /// </summary>
    Prioritized = 1,

    /// <summary>
    /// Messages overflow to the next available consumer.
    /// </summary>
    Overflow = 2,

    /// <summary>
    /// Consumer is pinned to a specific client.
    /// </summary>
    PinnedClient = 3,
}
