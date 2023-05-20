using System.Threading.Tasks.Sources;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class PublishCommand<T> : CommandBase<PublishCommand<T>>
{
    private NatsKey _subject;
    private T? _value;
    private INatsSerializer? _serializer;

    private PublishCommand()
    {
    }

    public static PublishCommand<T> Create(ObjectPool pool, CancellationTimer timer, in NatsKey subject, T? value, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PublishCommand<T>();
        }

        result._subject = subject;
        result._value = value;
        result._serializer = serializer;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(_subject, null, _value, _serializer!);
    }

    protected override void Reset()
    {
        _subject = default;
        _value = default;
        _serializer = null;
    }
}

internal sealed class AsyncPublishCommand<T> : AsyncCommandBase<AsyncPublishCommand<T>>
{
    private NatsKey _subject;
    private T? _value;
    private INatsSerializer? _serializer;

    private AsyncPublishCommand()
    {
    }

    public static AsyncPublishCommand<T> Create(ObjectPool pool, CancellationTimer timer, in NatsKey subject, T? value, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishCommand<T>();
        }

        result._subject = subject;
        result._value = value;
        result._serializer = serializer;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(_subject!, null, _value, _serializer!);
    }

    protected override void Reset()
    {
        _subject = default;
        _value = default;
        _serializer = null;
    }
}

internal sealed class PublishBatchCommand<T> : CommandBase<PublishBatchCommand<T>>, IBatchCommand
{
    private IEnumerable<(NatsKey subject, T? value)>? _values1;
    private IEnumerable<(string subject, T? value)>? _values2;
    private INatsSerializer? _serializer;

    private PublishBatchCommand()
    {
    }

    public static PublishBatchCommand<T> Create(ObjectPool pool, CancellationTimer timer, IEnumerable<(NatsKey subject, T? value)> values, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PublishBatchCommand<T>();
        }

        result._values1 = values;
        result._serializer = serializer;
        result.SetCancellationTimer(timer);

        return result;
    }

    public static PublishBatchCommand<T> Create(ObjectPool pool, CancellationTimer timer, IEnumerable<(string subject, T? value)> values, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PublishBatchCommand<T>();
        }

        result._values2 = values;
        result._serializer = serializer;
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
        if (_values1 != null)
        {
            foreach (var item in _values1)
            {
                writer.WritePublish(item.subject, null, item.value, _serializer!);
                i++;
            }
        }
        else if (_values2 != null)
        {
            foreach (var item in _values2)
            {
                writer.WritePublish(new NatsKey(item.subject, true), null, item.value, _serializer!);
                i++;
            }
        }

        return i;
    }

    protected override void Reset()
    {
        _values1 = default;
        _values2 = default;
        _serializer = null;
    }
}

internal sealed class AsyncPublishBatchCommand<T> : AsyncCommandBase<AsyncPublishBatchCommand<T>>, IBatchCommand
{
    private IEnumerable<(NatsKey subject, T? value)>? _values1;
    private IEnumerable<(string subject, T? value)>? _values2;
    private INatsSerializer? _serializer;

    private AsyncPublishBatchCommand()
    {
    }

    public static AsyncPublishBatchCommand<T> Create(ObjectPool pool, CancellationTimer timer, IEnumerable<(NatsKey subject, T? value)> values, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishBatchCommand<T>();
        }

        result._values1 = values;
        result._serializer = serializer;
        result.SetCancellationTimer(timer);

        return result;
    }

    public static AsyncPublishBatchCommand<T> Create(ObjectPool pool, CancellationTimer timer, IEnumerable<(string subject, T? value)> values, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishBatchCommand<T>();
        }

        result._values2 = values;
        result._serializer = serializer;
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
        if (_values1 != null)
        {
            foreach (var item in _values1)
            {
                writer.WritePublish(item.subject, null, item.value, _serializer!);
                i++;
            }
        }
        else if (_values2 != null)
        {
            foreach (var item in _values2)
            {
                writer.WritePublish(new NatsKey(item.subject, true), null, item.value, _serializer!);
                i++;
            }
        }

        return i;
    }

    protected override void Reset()
    {
        _values1 = default;
        _values2 = default;
        _serializer = null;
    }
}

internal sealed class PublishBytesCommand : CommandBase<PublishBytesCommand>
{
    private NatsKey _subject;
    private ReadOnlyMemory<byte> _value;

    private PublishBytesCommand()
    {
    }

    public static PublishBytesCommand Create(ObjectPool pool, CancellationTimer timer, in NatsKey subject, ReadOnlyMemory<byte> value)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PublishBytesCommand();
        }

        result._subject = subject;
        result._value = value;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(_subject, null, _value.Span);
    }

    protected override void Reset()
    {
        _subject = default;
        _value = default;
    }
}

internal sealed class AsyncPublishBytesCommand : AsyncCommandBase<AsyncPublishBytesCommand>
{
    private NatsKey _subject;
    private ReadOnlyMemory<byte> _value;

    private AsyncPublishBytesCommand()
    {
    }

    public static AsyncPublishBytesCommand Create(ObjectPool pool, CancellationTimer timer, in NatsKey subject, ReadOnlyMemory<byte> value)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishBytesCommand();
        }

        result._subject = subject;
        result._value = value;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(_subject!, null, _value.Span);
    }

    protected override void Reset()
    {
        _subject = default;
        _value = default;
    }
}
