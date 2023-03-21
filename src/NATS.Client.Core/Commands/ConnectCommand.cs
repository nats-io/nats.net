using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class AsyncConnectCommand : AsyncCommandBase<AsyncConnectCommand>
{
    private ClientOptions? _clientOptions;

    private AsyncConnectCommand()
    {
    }

    public static AsyncConnectCommand Create(ObjectPool pool, ClientOptions connectOptions)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncConnectCommand();
        }

        result._clientOptions = connectOptions;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteConnect(_clientOptions!);
    }

    protected override void Reset()
    {
        _clientOptions = null;
    }
}
