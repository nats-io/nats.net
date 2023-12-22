namespace NATS.Client.Core;

internal class NatsPooledConnection : NatsConnection
{
    public NatsPooledConnection(NatsOpts opts)
        : base(opts)
    {
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
