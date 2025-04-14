using System.Buffers;
using System.Collections.Concurrent;

namespace NATS.Client.Core.Internal;

internal class ReplyTask<T> : ReplyTaskBase, IDisposable
{
    private readonly ReplyTaskFactory _factory;
    private readonly long _id;
    private readonly string _subject;
    private readonly NatsConnection _connection;
    private readonly NatsHeaderParser _headerParser;
    private readonly INatsDeserialize<T> _deserializer;
    private readonly TimeSpan _requestTimeout;
    private readonly TaskCompletionSource _tcs;
    private NatsMsg<T> _msg;

    public ReplyTask(ReplyTaskFactory factory, long id, string subject, NatsConnection connection, NatsHeaderParser headerParser, INatsDeserialize<T> deserializer, TimeSpan requestTimeout)
    {
        _factory = factory;
        _id = id;
        _subject = subject;
        _connection = connection;
        _headerParser = headerParser;
        _deserializer = deserializer;
        _requestTimeout = requestTimeout;
        _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public string Subject => _subject;

    public async ValueTask<NatsMsg<T>> GetResultAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _tcs.Task
                .WaitAsync(_requestTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new NatsNoReplyException();
        }

        lock (this)
        {
            return _msg;
        }
    }

    public override void SetResult(string? replyTo, ReadOnlySequence<byte> payload, ReadOnlySequence<byte>? headersBuffer)
    {
        lock (this)
        {
            _msg = NatsMsg<T>.Build(Subject, replyTo, headersBuffer, payload, _connection, _headerParser, _deserializer);
        }

        _tcs.SetResult();
    }

    public void Dispose() => _factory.Return(_id);
}

internal class ReplyTaskBase
{
    public virtual void SetResult(string? replyTo, ReadOnlySequence<byte> payload, ReadOnlySequence<byte>? headersBuffer)
    {
    }
}

internal class ReplyTaskFactory
{
    private readonly NatsConnection _connection;
    private readonly string _inboxPrefix;
    private readonly NatsHeaderParser _headerParser;
    private readonly INatsSerializerRegistry _serializerRegistry;
    private readonly ConcurrentDictionary<long, ReplyTaskBase> _reply;
    private readonly TimeSpan _requestTimeout;
    private long _nextId;

    public ReplyTaskFactory(NatsConnection connection)
    {
        _connection = connection;
        _inboxPrefix = _connection.InboxPrefix + ".";
        _headerParser = _connection.HeaderParser;
        _requestTimeout = _connection.Opts.RequestTimeout;
        _serializerRegistry = _connection.Opts.SerializerRegistry;
        _reply = new ConcurrentDictionary<long, ReplyTaskBase>();
    }

    public ReplyTask<TReply> CreateReplyTask<TReply>(INatsDeserialize<TReply>? deserializer)
    {
        deserializer ??= _serializerRegistry.GetDeserializer<TReply>();
        var id = Interlocked.Increment(ref _nextId);
        var replyTo = _inboxPrefix + id;
        var rmb = new ReplyTask<TReply>(this, id, replyTo, _connection, _headerParser, deserializer, _requestTimeout);
        _reply[id] = rmb;
        return rmb;
    }

    public void Return(long id) => _reply.TryRemove(id, out _);

    public void SetResult(long id, string? replyTo, in ReadOnlySequence<byte> payloadBuffer, in ReadOnlySequence<byte>? headersBuffer)
    {
        if (_reply.TryGetValue(id, out var rmb))
        {
            rmb.SetResult(replyTo, payloadBuffer, headersBuffer);
        }
    }
}
