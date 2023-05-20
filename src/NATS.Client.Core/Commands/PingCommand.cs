using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class PingCommand : CommandBase<PingCommand>
{
    private PingCommand()
    {
    }

    public static PingCommand Create(ObjectPool pool, CancellationTimer timer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PingCommand();
        }

        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePing();
    }

    protected override void Reset()
    {
    }
}

internal sealed class AsyncPingCommand : AsyncCommandBase<AsyncPingCommand, TimeSpan>
{
    private NatsConnection? _connection;

    private AsyncPingCommand()
    {
    }

    public DateTimeOffset? WriteTime { get; private set; }

    public static AsyncPingCommand Create(NatsConnection connection, ObjectPool pool, CancellationTimer timer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPingCommand();
        }

        result._connection = connection;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        WriteTime = DateTimeOffset.UtcNow;
        _connection!.EnqueuePing(this);
        writer.WritePing();
    }

    protected override void Reset()
    {
        WriteTime = null;
        _connection = null;
    }
}
