using System.Buffers;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class AsyncPublishCommand<T> : AsyncCommandBase<AsyncPublishCommand<T>>
{
    private string? _subject;
    private string? _replyTo;
    private T? _value;
    private INatsSerializer? _serializer;

    private AsyncPublishCommand()
    {
    }

    public static AsyncPublishCommand<T> Create(ObjectPool pool, CancellationTimer timer, string subject, string? replyTo, T? value, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishCommand<T>();
        }

        result._subject = subject;
        result._replyTo = replyTo;
        result._value = value;
        result._serializer = serializer;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(_subject!, _replyTo, _value, _serializer!);
    }

    protected override void Reset()
    {
        _subject = default;
        _value = default;
        _serializer = null;
    }
}

internal sealed class AsyncPublishBytesCommand : AsyncCommandBase<AsyncPublishBytesCommand>
{
    private string? _subject;
    private ReadOnlySequence<byte> _value;

    private AsyncPublishBytesCommand()
    {
    }

    public static AsyncPublishBytesCommand Create(ObjectPool pool, CancellationTimer timer, string subject, ReadOnlySequence<byte> value)
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
        writer.WritePublish(_subject!, null, _value);
    }

    protected override void Reset()
    {
        _subject = default;
        _value = default;
    }
}
