using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Text;

namespace NATS.Client.Core.Internal;

internal sealed class ReplyTask<T> : ReplyTaskBase, IDisposable
{
    private readonly object _gate;
    private readonly ReplyTaskFactory _factory;
    private readonly long _subjectNum;
    private readonly NatsConnection _connection;
    private readonly INatsDeserialize<T> _deserializer;
    private readonly TimeSpan _requestTimeout;
    private readonly TaskCompletionSource _tcs;
    private NatsMsg<T> _msg;

    public ReplyTask(ReplyTaskFactory factory, long subjectNum, string subject, NatsConnection connection, INatsDeserialize<T> deserializer, TimeSpan requestTimeout)
    {
        _factory = factory;
        _subjectNum = subjectNum;
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
            NatsNoReplyException.Throw();
        }

        lock (_gate)
        {
            return _msg;
        }
    }

    public override void SetResult(NatsProcessProps props, ReadOnlySequence<byte> payload, ReadOnlySequence<byte>? headersBuffer)
    {
        lock (_gate)
        {
            _msg = NatsMsg<T>.Build(Subject, props.ReplyTo?.ToString(), headersBuffer, payload, _connection, _connection.HeaderParser, _deserializer);
        }

        _tcs.TrySetResult();
    }

    public void Dispose() => _factory.Return(_subjectNum);
}

internal abstract class ReplyTaskBase
{
    public abstract void SetResult(NatsProcessProps props, ReadOnlySequence<byte> payload, ReadOnlySequence<byte>? headersBuffer);
}

internal sealed class ReplyTaskFactory
{
    private readonly byte[] _inboxPrefix;
    private readonly string _inboxPrefixString;
    private readonly NatsConnection _connection;
    private readonly ConcurrentDictionary<long, ReplyTaskBase> _replies;
    private readonly INatsSerializerRegistry _serializerRegistry;
    private readonly TimeSpan _requestTimeout;
    private readonly int _subjectMaxLength;
    private readonly bool _allocSubject;
    private long _nextId;

    public ReplyTaskFactory(NatsConnection connection)
    {
        _connection = connection;
        _inboxPrefixString = _connection.InboxPrefix + ".";
        _inboxPrefix = Encoding.UTF8.GetBytes(_inboxPrefixString);
        _subjectMaxLength = _inboxPrefix.Length + 20; // 20 digits for long
        _allocSubject = _subjectMaxLength < 128;
        _serializerRegistry = _connection.Opts.SerializerRegistry;
        _requestTimeout = _connection.Opts.RequestTimeout;
        _replies = new ConcurrentDictionary<long, ReplyTaskBase>();
    }

    public ReplyTask<TReply> CreateReplyTask<TReply>(INatsDeserialize<TReply>? deserializer, TimeSpan? requestTimeout)
    {
        deserializer ??= _serializerRegistry.GetDeserializer<TReply>();
        var id = Interlocked.Increment(ref _nextId);
        
        string subject;
        if (_allocSubject)
        {
            Span<byte> buffer = stackalloc byte[_subjectMaxLength];
            _inboxPrefix.CopyTo(buffer);
            var idSpan = buffer.Slice(_inboxPrefix.Length);
            if (Utf8Formatter.TryFormat(id, idSpan, out var written))
            {
                var subjectSpan = buffer.Slice(0, written + _inboxPrefix.Length);
                subject = Encoding.UTF8.GetString(subjectSpan);
            }
            else
            {
                subject = _inboxPrefixString + id;
            }
        }
        else
        {
            subject = _inboxPrefixString + id;
        }

        var rt = new ReplyTask<TReply>(this, id, subject, _connection, deserializer, requestTimeout ?? _requestTimeout);
        _replies.TryAdd(id, rt);
        return rt;
    }

    public void Return(long id) => _replies.TryRemove(id, out _);

    public bool TrySetResult(NatsProcessProps props, in ReadOnlySequence<byte> payloadBuffer, in ReadOnlySequence<byte>? headersBuffer)
    {
        if (_replies.TryGetValue(props.SubjectNumber, out var rt))
        {
            rt.SetResult(props, payloadBuffer, headersBuffer);
            return true;
        }

        return false;
    }
}
