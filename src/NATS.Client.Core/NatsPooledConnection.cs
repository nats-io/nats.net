namespace NATS.Client.Core;

internal sealed class NatsPooledConnection : NatsConnection
{
    public NatsPooledConnection(NatsOpts opts)
        : base(opts)
    {
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal ValueTask ForceDisposeAsync() => base.DisposeAsync();
}
