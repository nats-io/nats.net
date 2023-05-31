using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class UnsubscribeCommand : CommandBase<UnsubscribeCommand>
{
    private int _subscriptionId;

    private UnsubscribeCommand()
    {
    }

    // Unsubscribe is fire-and-forget, don't use CancellationTimer.
    public static UnsubscribeCommand Create(ObjectPool pool, int subscriptionId)
    {
        if (!TryRent(pool, out var result))
        {
            result = new UnsubscribeCommand();
        }

        result._subscriptionId = subscriptionId;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteUnsubscribe(_subscriptionId, null);
    }

    protected override void Reset()
    {
        _subscriptionId = 0;
    }
}
