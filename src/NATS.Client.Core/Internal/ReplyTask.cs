using System.Buffers;
using System.Collections.Concurrent;

namespace NATS.Client.Core.Internal;

internal class ReplyTask<T> : ReplyTaskBase, IDisposable
{
    private readonly object _gate;
    private readonly ReplyTaskFactory _factory;
    private readonly long _id;
    private readonly NatsConnection _connection;
    private readonly INatsDeserialize<T> _deserializer;
    private readonly TimeSpan _requestTimeout;
    private readonly TaskCompletionSource _tcs;
    private NatsMsg<T> _msg;

    public ReplyTask(ReplyTaskFactory factory, long id, string subject, NatsConnection connection, INatsDeserialize<T> deserializer, TimeSpan requestTimeout)
    {
        _factory = factory;
        _id = id;
        Subject = subject;
        _connection = connection;
        _deserializer = deserializer;
        _requestTimeout = requestTimeout;
        _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate = new object();
    }

    public string Subject { get; }

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

        lock (_gate)
        {
            return _msg;
        }
    }

    public override void SetResult(string? replyTo, ReadOnlySequence<byte> payload, ReadOnlySequence<byte>? headersBuffer)
    {
        lock (_gate)
        {
            _msg = NatsMsg<T>.Build(Subject, replyTo, headersBuffer, payload, _connection, _connection.HeaderParser, _deserializer);
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
    private readonly string _inboxPrefix;
    private readonly NatsConnection _connection;
    private readonly ConcurrentDictionary<long, ReplyTaskBase> _reply;
    private long _nextId;

    public ReplyTaskFactory(NatsConnection connection)
    {
        _connection = connection;
        _inboxPrefix = _connection.InboxPrefix + ".";
        _reply = new ConcurrentDictionary<long, ReplyTaskBase>();
    }

    public ReplyTask<TReply> CreateReplyTask<TReply>(INatsDeserialize<TReply>? deserializer, TimeSpan? requestTimeout)
    {
        deserializer ??= _connection.Opts.SerializerRegistry.GetDeserializer<TReply>();
        var id = Interlocked.Increment(ref _nextId);
        var subject = _inboxPrefix + id;
        var rmb = new ReplyTask<TReply>(this, id, subject, _connection, deserializer, requestTimeout ?? _connection.Opts.RequestTimeout);
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
