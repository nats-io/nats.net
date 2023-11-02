using NATS.Client.Core;

namespace NATS.Client.KeyValueStore;

/// <summary>
/// Key Value Store options
/// </summary>
public record NatsKVOpts
{
}

/// <summary>
/// Key Value Store watch options
/// </summary>
public record NatsKVWatchOpts
{
    /// <summary>
    /// Default watch options
    /// </summary>
    public static readonly NatsKVWatchOpts Default = new();

    /// <summary>
    /// Idle heartbeat interval
    /// </summary>
    public TimeSpan IdleHeartbeat { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ignore deletes of the entries
    /// </summary>
    public bool IgnoreDeletes { get; init; } = false;

    /// <summary>
    /// Include history of the entries
    /// </summary>
    public bool IncludeHistory { get; init; } = false;

    /// <summary>
    /// Only retrieve updates, not current values
    /// </summary>
    public bool UpdatesOnly { get; init; } = false;

    /// <summary>
    /// Meta only to get the metadata of the entries
    /// </summary>
    public bool MetaOnly { get; init; } = false;
}
