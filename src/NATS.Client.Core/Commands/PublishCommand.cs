using System.Buffers;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class AsyncPublishCommand<T> : AsyncCommandBase<AsyncPublishCommand<T>>
{
    private string? _subject;
    private string? _replyTo;
    private NatsHeaders? _headers;
    private T? _value;
    private INatsSerializer? _serializer;

    private AsyncPublishCommand()
    {
    }

    public static AsyncPublishCommand<T> Create(ObjectPool pool, CancellationTimer timer, string subject, string? replyTo, NatsHeaders? headers, T? value, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishCommand<T>();
        }

        result._subject = subject;
        result._replyTo = replyTo;
        result._headers = headers;
        result._value = value;
        result._serializer = serializer;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(_subject!, _replyTo, _headers, _value, _serializer!);
    }

    protected override void Reset()
    {
        _subject = default;
        _headers = default;
        _value = default;
        _serializer = null;
    }
}

internal sealed class AsyncPublishBytesCommand : AsyncCommandBase<AsyncPublishBytesCommand>
{
    private string? _subject;
    private string? _replyTo;
    private NatsHeaders? _headers;
    private ReadOnlySequence<byte> _payload;

    private AsyncPublishBytesCommand()
    {
    }

    public static AsyncPublishBytesCommand Create(ObjectPool pool, CancellationTimer timer, string subject, string? replyTo, NatsHeaders? headers, ReadOnlySequence<byte> payload)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPublishBytesCommand();
        }

        result._subject = subject;
        result._replyTo = replyTo;
        result._headers = headers;
        result._payload = payload;
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(_subject!, _replyTo, _headers, _payload);
    }

    protected override void Reset()
    {
        _subject = default;
        _replyTo = default;
        _headers = default;
        _payload = default;
    }
}
