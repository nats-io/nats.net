using System.Threading.Tasks.Sources;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class PublishCommand<T> : CommandBase<PublishCommand<T>>
{
    NatsKey subject;
    T? value;
    INatsSerializer? serializer;

    PublishCommand()
    {
    }

    public static PublishCommand<T> Create(ObjectPool pool, in NatsKey subject, T? value, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PublishCommand<T>();
        }

        result.subject = subject;
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject, null, value, serializer!);
    }

    protected override void Reset()
    {
        subject = default;
        value = default;
        serializer = null;
    }
}

internal sealed class AsyncPublishCommand<T> : AsyncCommandBase<AsyncPublishCommand<T>>
{
    NatsKey subject;
    T? value;
    INatsSerializer? serializer;

    AsyncPublishCommand()
    {
    }

    public static AsyncPublishCommand<T> Create(ObjectPool pool, in NatsKey subject, T? value, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishCommand<T>();
        }

        result.subject = subject;
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject!, null, value, serializer!);
    }

    protected override void Reset()
    {
        subject = default;
        value = default;
        serializer = null;
    }
}

internal sealed class PublishBatchCommand<T> : CommandBase<PublishBatchCommand<T>>, IBatchCommand
{
    IEnumerable<(NatsKey subject, T? value)>? values1;
    IEnumerable<(string subject, T? value)>? values2;
    INatsSerializer? serializer;

    PublishBatchCommand()
    {
    }

    public static PublishBatchCommand<T> Create(ObjectPool pool, IEnumerable<(NatsKey subject, T? value)> values, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PublishBatchCommand<T>();
        }

        result.values1 = values;
        result.serializer = serializer;

        return result;
    }

    public static PublishBatchCommand<T> Create(ObjectPool pool, IEnumerable<(string subject, T? value)> values, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PublishBatchCommand<T>();
        }

        result.values2 = values;
        result.serializer = serializer;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        (this as IBatchCommand).Write(writer);
    }


    int IBatchCommand.Write(ProtocolWriter writer)
    {
        var i = 0;
        if (values1 != null)
        {
            foreach (var item in values1)
            {
                writer.WritePublish(item.subject, null, item.value, serializer!);
                i++;
            }
        }
        else if (values2 != null)
        {
            foreach (var item in values2)
            {
                writer.WritePublish(new NatsKey(item.subject, true), null, item.value, serializer!);
                i++;
            }
        }
        return i;
    }

    protected override void Reset()
    {
        values1 = default;
        values2 = default;
        serializer = null;
    }
}

internal sealed class AsyncPublishBatchCommand<T> : AsyncCommandBase<AsyncPublishBatchCommand<T>>, IBatchCommand
{
    IEnumerable<(NatsKey subject, T? value)>? values1;
    IEnumerable<(string subject, T? value)>? values2;
    INatsSerializer? serializer;

    AsyncPublishBatchCommand()
    {
    }

    public static AsyncPublishBatchCommand<T> Create(ObjectPool pool, IEnumerable<(NatsKey subject, T? value)> values, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishBatchCommand<T>();
        }

        result.values1 = values;
        result.serializer = serializer;

        return result;
    }

    public static AsyncPublishBatchCommand<T> Create(ObjectPool pool, IEnumerable<(string subject, T? value)> values, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishBatchCommand<T>();
        }

        result.values2 = values;
        result.serializer = serializer;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        (this as IBatchCommand).Write(writer);
    }


    int IBatchCommand.Write(ProtocolWriter writer)
    {
        var i = 0;
        if (values1 != null)
        {
            foreach (var item in values1)
            {
                writer.WritePublish(item.subject, null, item.value, serializer!);
                i++;
            }
        }
        else if (values2 != null)
        {
            foreach (var item in values2)
            {
                writer.WritePublish(new NatsKey(item.subject, true), null, item.value, serializer!);
                i++;
            }
        }
        return i;
    }

    protected override void Reset()
    {
        values1 = default;
        values2 = default;
        serializer = null;
    }
}

internal sealed class PublishBytesCommand : CommandBase<PublishBytesCommand>
{
    NatsKey subject;
    ReadOnlyMemory<byte> value;

    PublishBytesCommand()
    {
    }

    public static PublishBytesCommand Create(ObjectPool pool, in NatsKey subject, ReadOnlyMemory<byte> value)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PublishBytesCommand();
        }

        result.subject = subject;
        result.value = value;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject, null, value.Span);
    }

    protected override void Reset()
    {
        subject = default;
        value = default;
    }
}

internal sealed class AsyncPublishBytesCommand : AsyncCommandBase<AsyncPublishBytesCommand>
{
    NatsKey subject;
    ReadOnlyMemory<byte> value;

    AsyncPublishBytesCommand()
    {
    }

    public static AsyncPublishBytesCommand Create(ObjectPool pool, in NatsKey subject, ReadOnlyMemory<byte> value)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishBytesCommand();
        }

        result.subject = subject;
        result.value = value;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject!, null, value.Span);
    }

    protected override void Reset()
    {
        subject = default;
        value = default;
    }
}
