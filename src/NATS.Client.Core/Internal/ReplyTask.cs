using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Text;

namespace NATS.Client.Core.Internal;

internal sealed class ReplyTask : ReplyTaskBase, IDisposable
{
    private readonly object _gate;
    private readonly ReplyTaskFactory _factory;
    private readonly long _id;
    private readonly TimeSpan _requestTimeout;
    private readonly TaskCompletionSource _tcs;
    private NatsRecievedEvent _msg;

    public ReplyTask(ReplyTaskFactory factory, long id, string subject, TimeSpan requestTimeout)
    {
        _factory = factory;
        _id = id;
        Subject = subject;
        _requestTimeout = requestTimeout;
        _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate = new object();
    }

    public string Subject { get; }

    public async ValueTask<NatsRecievedEvent> GetResultAsync(CancellationToken cancellationToken)
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

    public override void SetResult(string? replyTo, ReadOnlySequence<byte> payload, ReadOnlySequence<byte>? headersBuffer)
    {
        lock (_gate)
        {
            var payloadValue = ReadOnlySequence<byte>.Empty;
            if (payload.Length > 0)
            {
                var payloadData = new byte[payload.Length];
                payload.CopyTo(payloadData);
                payloadValue = new ReadOnlySequence<byte>(payloadData);
            }

            ReadOnlySequence<byte>? headerValue = null;
            if (headersBuffer != null)
            {
                var headerData = new byte[headersBuffer.Value.Length];
                headersBuffer.Value.CopyTo(headerData);
                headerValue = new ReadOnlySequence<byte>(headerData);
            }

            _msg = new NatsRecievedEvent(Subject, replyTo, headersBuffer, payload);
        }

        _tcs.TrySetResult();
    }

    public void Dispose() => _factory.Return(_id);
}

internal abstract class ReplyTaskBase
{
    public abstract void SetResult(string? replyTo, ReadOnlySequence<byte> payload, ReadOnlySequence<byte>? headersBuffer);
}

internal sealed class ReplyTaskFactory
{
    private readonly byte[] _inboxPrefix;
    private readonly string _inboxPrefixString;
    private readonly NatsConnection _connection;
    private readonly ConcurrentDictionary<long, ReplyTaskBase> _replies;
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
        _requestTimeout = _connection.Opts.RequestTimeout;
        _replies = new ConcurrentDictionary<long, ReplyTaskBase>();
    }

    public ReplyTask CreateReplyTask(TimeSpan? requestTimeout)
    {
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

        var rt = new ReplyTask(this, id, subject, requestTimeout ?? _requestTimeout);
        _replies.TryAdd(id, rt);
        return rt;
    }

    public void Return(long id) => _replies.TryRemove(id, out _);

    public bool TrySetResult(long id, string? replyTo, in ReadOnlySequence<byte> payloadBuffer, in ReadOnlySequence<byte>? headersBuffer)
    {
        if (_replies.TryGetValue(id, out var rt))
        {
            rt.SetResult(replyTo, payloadBuffer, headersBuffer);
            return true;
        }

        return false;
    }
}
