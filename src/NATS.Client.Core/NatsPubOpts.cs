namespace NATS.Client.Core;

public readonly record struct NatsPubOpts
{
    public string? ReplyTo { get; init; }

    // TODO: Implement headers in NatsPubOpts
    // public NatsHeaders? Headers
    // {
    //     get => throw new NotImplementedException();
    //     init => throw new NotImplementedException();
    // }
    public INatsSerializer? Serializer { get; init; }
}
