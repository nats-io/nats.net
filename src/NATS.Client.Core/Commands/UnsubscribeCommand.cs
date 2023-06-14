using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class UnsubscribeCommand : CommandBase<UnsubscribeCommand>
{
    private int _sid;

    private UnsubscribeCommand()
    {
    }

    // Unsubscribe is fire-and-forget, don't use CancellationTimer.
    public static UnsubscribeCommand Create(ObjectPool pool, int sid)
    {
        if (!TryRent(pool, out var result))
        {
            result = new UnsubscribeCommand();
        }

        result._sid = sid;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteUnsubscribe(_sid, null);
    }

    protected override void Reset()
    {
        _sid = 0;
    }
}
