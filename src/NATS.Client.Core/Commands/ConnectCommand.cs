using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class AsyncConnectCommand : AsyncCommandBase<AsyncConnectCommand>
{
    private ClientOpts? _clientOpts;

    private AsyncConnectCommand()
    {
    }

    public static AsyncConnectCommand Create(ObjectPool pool, ClientOpts connectOpts, CancellationTimer timer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncConnectCommand();
        }

        result._clientOpts = connectOpts;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteConnect(_clientOpts!);
    }

    protected override void Reset()
    {
        _clientOpts = null;
    }
}
