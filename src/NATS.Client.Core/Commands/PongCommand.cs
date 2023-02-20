using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class PongCommand : CommandBase<PongCommand>
{
    PongCommand()
    {
    }

    public static PongCommand Create(ObjectPool pool)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PongCommand();
        }
        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePong();
    }

    protected override void Reset()
    {
    }
}
