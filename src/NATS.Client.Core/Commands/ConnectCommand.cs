using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class AsyncConnectCommand : AsyncCommandBase<AsyncConnectCommand>
{
    ConnectOptions? connectOptions;

    AsyncConnectCommand()
    {
    }

    public static AsyncConnectCommand Create(ObjectPool pool, ConnectOptions connectOptions)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncConnectCommand();
        }

        result.connectOptions = connectOptions;

        return result;
    }

    protected override void Reset()
    {
        connectOptions = null;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteConnect(connectOptions!);
    }
}
