using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class AsyncSubscribeCommand : AsyncCommandBase<AsyncSubscribeCommand>
{
    private string? _subject;
    private string? _queueGroup;
    private int _sid;

    private AsyncSubscribeCommand()
    {
    }

    public static AsyncSubscribeCommand Create(ObjectPool pool, CancellationTimer timer, int sid, string subject, string? queueGroup)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncSubscribeCommand();
        }

        result._subject = subject;
        result._sid = sid;
        result._queueGroup = queueGroup;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteSubscribe(_sid, _subject!, _queueGroup);
    }

    protected override void Reset()
    {
        _subject = default;
        _queueGroup = default;
        _sid = 0;
    }
}

internal sealed class AsyncSubscribeBatchCommand : AsyncCommandBase<AsyncSubscribeBatchCommand>, IBatchCommand
{
    private (int sid, string subject, string? queueGroup)[]? _subscriptions;

    private AsyncSubscribeBatchCommand()
    {
    }

    public static AsyncSubscribeBatchCommand Create(ObjectPool pool, CancellationTimer timer, (int sid, string subject, string? queueGroup)[]? subscriptions)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncSubscribeBatchCommand();
        }

        result._subscriptions = subscriptions;
        result.SetCancellationTimer(timer);

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
                writer.WriteSubscribe(id, subject, queue);
            }
        }

        return i;
    }

    protected override void Reset()
    {
        _subscriptions = default;
    }
}
