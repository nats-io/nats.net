namespace NATS.Client.Core;

internal sealed class NatsPooledConnection : NatsConnection
{
    public NatsPooledConnection(NatsOpts opts)
        : base(opts)
    {
    }

    public override ValueTask DisposeAsync() => default;

    internal ValueTask ForceDisposeAsync() => base.DisposeAsync();
}
