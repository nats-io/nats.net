using NATS.Client.Core;

namespace NATS.Client.KeyValueStore;

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
    /// <remarks>
    /// Setting this will cause the watcher to throw <see cref="InvalidOperationException"/>
    /// if the values for <see cref="UpdatesOnly"/> and/or <see cref="ResumeAtRevision"/> are set.
    /// </remarks>
    public bool IncludeHistory { get; init; } = false;

    /// <summary>
    /// Only retrieve updates, not current values
    /// </summary>
    /// <remarks>
    /// Setting this will cause the watcher to throw <see cref="InvalidOperationException"/>
    /// if the values for <see cref="IncludeHistory"/> and/or <see cref="ResumeAtRevision"/> are set.
    /// </remarks>
    public bool UpdatesOnly { get; init; } = false;

    /// <summary>
    /// Meta only to get the metadata of the entries
    /// </summary>
    public bool MetaOnly { get; init; } = false;

    /// <summary>
    /// Async function called when the enumerator reaches the end of data. Return True to break the async enumeration, False to allow the enumeration to continue.
    /// </summary>
    public Func<CancellationToken, ValueTask<bool>>? OnNoData { get; init; }

    /// <summary>
    /// The revision to start from, if set to 0 (default) this will be ignored.
    /// <remarks>
    /// Setting this to a non-zero value will cause the watcher to throw <see cref="InvalidOperationException"/>
    /// if the values for <see cref="IncludeHistory"/> and/or <see cref="UpdatesOnly"/> are set.
    /// </remarks>
    /// </summary>
    public ulong ResumeAtRevision { get; init; }

    internal void ThrowIfInvalid()
    {
        if (ResumeAtRevision > 0)
        {
            if (IncludeHistory || UpdatesOnly)
            {
                throw new InvalidOperationException("IncludeHistory and UpdatesOnly are only valid when ResumeAtRevision is set to a non-zero value.");
            }
        }
        else
        {
            if (IncludeHistory && UpdatesOnly)
            {
                throw new InvalidOperationException("IncludeHistory and UpdatesOnly are mutually exclusive.");
            }
        }
    }
}

public record NatsKVDeleteOpts
{
    public bool Purge { get; init; }

    public ulong Revision { get; init; }
}

public record NatsKVPurgeOpts
{
    public static readonly NatsKVPurgeOpts Default = new() { DeleteMarkersThreshold = TimeSpan.FromMinutes(30) };

    public TimeSpan DeleteMarkersThreshold { get; init; }
}
