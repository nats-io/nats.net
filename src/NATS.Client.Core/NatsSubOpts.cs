namespace NATS.Client.Core;

public readonly record struct NatsSubOpts
{
    public string? QueueGroup { get; init; }

    public INatsSerializer? Serializer { get; init; }
}
