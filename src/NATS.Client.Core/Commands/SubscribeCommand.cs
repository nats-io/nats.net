using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class SubscribeCommand : CommandBase<SubscribeCommand>
{
    private NatsKey _subject;
    private NatsKey? _queueGroup;
    private int _subscriptionId;

    private SubscribeCommand()
    {
    }

    public static SubscribeCommand Create(ObjectPool pool, int subscriptionId, in NatsKey subject, in NatsKey? queueGroup)
    {
        if (!TryRent(pool, out var result))
        {
            result = new SubscribeCommand();
        }

        result._subject = subject;
        result._subscriptionId = subscriptionId;
        result._queueGroup = queueGroup;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteSubscribe(_subscriptionId, _subject, _queueGroup);
    }

    protected override void Reset()
    {
        _subject = default;
        _queueGroup = default;
        _subscriptionId = 0;
    }
}

internal sealed class AsyncSubscribeCommand : AsyncCommandBase<AsyncSubscribeCommand>
{
    private NatsKey _subject;
    private NatsKey? _queueGroup;
    private int _subscriptionId;

    private AsyncSubscribeCommand()
    {
    }

    public static AsyncSubscribeCommand Create(ObjectPool pool, int subscriptionId, in NatsKey subject, in NatsKey? queueGroup)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncSubscribeCommand();
        }

        result._subject = subject;
        result._subscriptionId = subscriptionId;
        result._queueGroup = queueGroup;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteSubscribe(_subscriptionId, _subject, _queueGroup);
    }

    protected override void Reset()
    {
        _subject = default;
        _queueGroup = default;
        _subscriptionId = 0;
    }
}

internal sealed class AsyncSubscribeBatchCommand : AsyncCommandBase<AsyncSubscribeBatchCommand>, IBatchCommand
{
    private (int subscriptionId, string subject, NatsKey? queueGroup)[]? _subscriptions;

    private AsyncSubscribeBatchCommand()
    {
    }

    public static AsyncSubscribeBatchCommand Create(ObjectPool pool, (int subscriptionId, string subject, NatsKey? queueGroup)[] subscriptions)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncSubscribeBatchCommand();
        }

        result._subscriptions = subscriptions;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        (this as IBatchCommand).Write(writer);
    }

    int IBatchCommand.Write(ProtocolWriter writer)
    {
        var i = 0;
        if (_subscriptions != null)
        {
            foreach (var (id, subject, queue) in _subscriptions)
            {
                i++;
                writer.WriteSubscribe(id, new NatsKey(subject, true), queue);
            }
        }

        return i;
    }

    protected override void Reset()
    {
        _subscriptions = default;
    }
}
