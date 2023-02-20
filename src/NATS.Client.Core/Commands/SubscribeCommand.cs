using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class SubscribeCommand : CommandBase<SubscribeCommand>
{
    NatsKey subject;
    NatsKey? queueGroup;
    int subscriptionId;

    SubscribeCommand()
    {
    }

    public static SubscribeCommand Create(ObjectPool pool, int subscriptionId, in NatsKey subject, in NatsKey? queueGroup)
    {
        if (!TryRent(pool, out var result))
        {
            result = new SubscribeCommand();
        }

        result.subject = subject;
        result.subscriptionId = subscriptionId;
        result.queueGroup = queueGroup;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteSubscribe(subscriptionId, subject, queueGroup);
    }

    protected override void Reset()
    {
        subject = default;
        queueGroup = default;
        subscriptionId = 0;
    }
}

internal sealed class AsyncSubscribeCommand : AsyncCommandBase<AsyncSubscribeCommand>
{
    NatsKey subject;
    NatsKey? queueGroup;
    int subscriptionId;

    AsyncSubscribeCommand()
    {
    }

    public static AsyncSubscribeCommand Create(ObjectPool pool, int subscriptionId, in NatsKey subject, in NatsKey? queueGroup)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncSubscribeCommand();
        }

        result.subject = subject;
        result.subscriptionId = subscriptionId;
        result.queueGroup = queueGroup;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteSubscribe(subscriptionId, subject, queueGroup);
    }

    protected override void Reset()
    {
        subject = default;
        queueGroup = default;
        subscriptionId = 0;
    }
}

internal sealed class AsyncSubscribeBatchCommand : AsyncCommandBase<AsyncSubscribeBatchCommand>, IBatchCommand
{
    (int subscriptionId, string subject, NatsKey? queueGroup)[]? subscriptions;

    AsyncSubscribeBatchCommand()
    {
    }

    public static AsyncSubscribeBatchCommand Create(ObjectPool pool, (int subscriptionId, string subject, NatsKey? queueGroup)[] subscriptions)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncSubscribeBatchCommand();
        }

        result.subscriptions = subscriptions;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        (this as IBatchCommand).Write(writer);
    }

    int IBatchCommand.Write(ProtocolWriter writer)
    {
        var i = 0;
        if (subscriptions != null)
        {
            foreach (var (id, subject, queue) in subscriptions)
            {
                i++;
                writer.WriteSubscribe(id, new NatsKey(subject, true), queue);
            }
        }
        return i;
    }

    protected override void Reset()
    {
        subscriptions = default;
    }
}
