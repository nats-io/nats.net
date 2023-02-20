using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class UnsubscribeCommand : CommandBase<UnsubscribeCommand>
{
    int subscriptionId;

    UnsubscribeCommand()
    {
    }

    public static UnsubscribeCommand Create(ObjectPool pool, int subscriptionId)
    {
        if (!TryRent(pool, out var result))
        {
            result = new UnsubscribeCommand();
        }

        result.subscriptionId = subscriptionId;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteUnsubscribe(subscriptionId, null);
    }

    protected override void Reset()
    {
        subscriptionId = 0;
    }
}
