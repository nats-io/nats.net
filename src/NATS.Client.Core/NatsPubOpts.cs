namespace NATS.Client.Core;

public readonly record struct NatsPubOpts
{
    public string? ReplyTo { get; init; }

    public NatsHeaders? Headers { get; init; }

    public INatsSerializer? Serializer { get; init; }
}
