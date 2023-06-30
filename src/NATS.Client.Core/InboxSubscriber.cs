using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

internal class InboxSubscriber : INatsSubBuilder<InboxSub>, IAsyncDisposable
{
    private readonly ILogger<InboxSubscriber> _logger;
    private readonly string? _queueGroup;
    private readonly ConcurrentDictionary<string, MsgWrapper> _writers = new();
    private readonly string _prefix;
    private InboxSub? _sub;
    private bool _started;

    public InboxSubscriber(
        NatsConnection connection,
        string? queueGroup = default,
        string prefix1 = "_INBOX",
        string? prefix2 = default)
    {
        _logger = connection.Options.LoggerFactory.CreateLogger<InboxSubscriber>();
        _queueGroup = queueGroup;
        prefix2 ??= Guid.NewGuid().ToString("N");
        _prefix = $"{prefix1}.{prefix2}";
        Connection = connection;
    }

    private NatsConnection Connection { get; }

    public async ValueTask EnsureStartedAsync()
    {
        // Only one call to this method can start the subscription.
        lock (this)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        try
        {
            _sub = await Connection.SubAsync($"{_prefix}.*", _queueGroup, this)
                .ConfigureAwait(false);
        }
        catch
        {
            // There is a race here when there are two or more calls to this method and
            // the first one fails to subscribe, other calls might carry on without exception.
            // While first call would produce the correct exception, subsequent calls will only
            // fail with a timeout. We reset here to allow retries to subscribe again.
            lock (this) _started = false;
            throw;
        }
    }

    public InboxSub Build(string subject, string? queueGroup, NatsConnection connection, SubscriptionManager manager)
    {
        var sid = manager.GetNextSid();
        return new InboxSub(this, subject, queueGroup, sid, connection, manager);
    }

    public string Register(MsgWrapper msg, string? suffix = null)
    {
        suffix ??= Guid.NewGuid().ToString("N");
        var subject = $"{_prefix}.{suffix}";
        if (!_writers.TryAdd(subject, msg))
            throw new InvalidOperationException("Subject already registered");
        return subject;
    }

    public void Unregister(string subject) => _writers.TryRemove(subject, out _);

    public void Received(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer, NatsConnection connection)
    {
        if (!_writers.TryGetValue(subject, out var msgWrapper))
        {
            _logger.LogWarning("Unregistered message inbox received");
            return;
        }

        msgWrapper.MsgReceived(subject, replyTo, headersBuffer, payloadBuffer, connection);
    }

    public ValueTask DisposeAsync()
    {
        if (_sub != null)
            return _sub.DisposeAsync();
        return ValueTask.CompletedTask;
    }
}

internal class InboxSub : INatsSub
{
    private readonly InboxSubscriber _inbox;
    private readonly NatsConnection _connection;
    private readonly SubscriptionManager _manager;

    public InboxSub(
        InboxSubscriber inbox,
        string subject,
        string? queueGroup,
        int sid,
        NatsConnection connection,
        SubscriptionManager manager)
    {
        _inbox = inbox;
        _connection = connection;
        _manager = manager;
        Subject = subject;
        QueueGroup = queueGroup;
        Sid = sid;
    }

    public string Subject { get; }

    public string? QueueGroup { get; }

    public int Sid { get; }

    public ValueTask ReceiveAsync(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer)
    {
        _inbox.Received(subject, replyTo, headersBuffer, payloadBuffer, _connection);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _manager.RemoveAsync(Sid);
}

internal class MsgWrapper : IValueTaskSource<object?>, IObjectPoolNode<MsgWrapper>
{
    private ManualResetValueTaskSourceCore<object?> _core = new()
    {
        RunContinuationsAsynchronously = true,
    };

    private INatsSerializer? _serializer;
    private Type? _type;
    private MsgWrapper? _next;
    private CancellationTokenRegistration _ctr;

    public ref MsgWrapper? NextNode => ref _next;

    public void SetSerializer<TData>(INatsSerializer serializer, CancellationToken cancellationToken)
    {
        _serializer = serializer;
        _type = typeof(TData);
        _ctr = cancellationToken.UnsafeRegister(
            static (msgWrapper, cancellationToken) => ((MsgWrapper)msgWrapper!).Cancel(cancellationToken), this);
    }

    public void MsgReceived(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer, NatsConnection connection)
    {
        if (_serializer == null || _type == null)
            throw new NullReferenceException("Serializer must be set");
        var data = _serializer.Deserialize(payloadBuffer, _type);
        _core.SetResult(data);
    }

    public ValueTask<object?> MsgRetrieveAsync() => new(this, _core.Version);

    public object? GetResult(short token)
    {
        _ctr.Dispose();
        lock (this)
        {
            try
            {
                return _core.GetResult(token);
            }
            finally
            {
                _core.Reset();
                _serializer = default;
                _type = default;
                _ctr = default;
            }
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);

    private void Cancel(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            _core.SetException(ExceptionDispatchInfo.SetCurrentStackTrace(new OperationCanceledException(cancellationToken)));
        }
    }
}
