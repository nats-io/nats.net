using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class AsyncFlushCommand : AsyncCommandBase<AsyncFlushCommand>
{
    private AsyncFlushCommand()
    {
    }

    public static AsyncFlushCommand Create(ObjectPool pool, CancellationTimer timer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncFlushCommand();
        }

        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
    }

    protected override void Reset()
    {
    }
}
