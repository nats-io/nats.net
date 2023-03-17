using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class AsyncConnectCommand : AsyncCommandBase<AsyncConnectCommand>
{
    ClientOptions? clientOptions;

    AsyncConnectCommand()
    {
    }

    public static AsyncConnectCommand Create(ObjectPool pool, ClientOptions connectOptions)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncConnectCommand();
        }

        result.clientOptions = connectOptions;

        return result;
    }

    protected override void Reset()
    {
        clientOptions = null;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteConnect(clientOptions!);
    }
}
