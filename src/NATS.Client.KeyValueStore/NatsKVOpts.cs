using NATS.Client.Core;

namespace NATS.Client.KeyValueStore;

public record NatsKVOpts
{
    public INatsSerializer? Serializer { get; init; }
}

public record NatsKVWatchOpts
{
    public static readonly NatsKVWatchOpts Default = new();

    public TimeSpan IdleHeartbeat { get; init; } = TimeSpan.FromSeconds(5);

    public bool IgnoreDeletes { get; init; } = false;

    public bool IncludeHistory { get; init; } = false;

    public bool UpdatesOnly { get; init; } = false;

    public bool MetaOnly { get; init; } = false;
}
