using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Internal;

internal enum NatsMsgCarrierTermination
{
    None,
    MaxMsgCount,
    Timeout,
    StartUpTimeout,
    IdleTimeout,
}

internal readonly record struct NatsMsgCarrier(
    string Subject,
    string? ReplyTo,
    NatsHeaders? Headers,
    INatsConnection? Connection,
    NatsMsgCarrierTermination Termination,
    object? ModelData,
    ReadOnlyMemory<byte>? BufferData)
{
    internal NatsMsg ToMsg() => new(Subject, ReplyTo, Headers, BufferData!.Value, Connection);

    internal NatsMsg<T> ToMsg<T>() => new(Subject, ReplyTo, Headers, (T?)ModelData, Connection);
}

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
            _sub = await Connection.SubAsync($"{_prefix}.*", new NatsSubOpts { QueueGroup = _queueGroup }, builder: this)
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

    public InboxSub Build(string subject, NatsSubOpts? opts, NatsConnection connection, SubscriptionManager manager)
    {
        var sid = manager.GetNextSid();
        return new InboxSub(this, subject, opts, sid, connection, manager);
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
        NatsSubOpts? opts,
        int sid,
        NatsConnection connection,
        SubscriptionManager manager)
    {
        _inbox = inbox;
        _connection = connection;
        _manager = manager;
        Subject = subject;
        QueueGroup = opts?.QueueGroup;
        PendingMsgs = opts?.MaxMsgs;
        Sid = sid;
    }

    public string Subject { get; }

    public string? QueueGroup { get; }

    public int? PendingMsgs { get; }

    public int Sid { get; }

    public void Ready()
    {
    }

    public ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        _inbox.Received(subject, replyTo, headersBuffer, payloadBuffer, _connection);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _manager.RemoveAsync(Sid);
}

internal class MsgWrapper : IValueTaskSource<NatsMsgCarrier>, IObjectPoolNode<MsgWrapper>
{
    private readonly Timer _timer1;
    private readonly Timer _timer2;
    private readonly Timer _timer3;
    private ManualResetValueTaskSourceCore<NatsMsgCarrier> _core;
    private INatsSerializer? _serializer;
    private Type? _type;
    private MsgWrapper? _next;
    private CancellationTokenRegistration _ctr;
    private bool _isSet;
    private TimeSpan? _timeout1;
    private TimeSpan? _timeout2;
    private TimeSpan? _timeout3;
    private short _timerVersion;
    private int _msgCount;
    private int _maxMsgCount;

    public MsgWrapper()
    {
        _core = new ManualResetValueTaskSourceCore<NatsMsgCarrier> { RunContinuationsAsynchronously = true };

        // Make suze _timerVersion != _core.Version until initialized
        _timerVersion = (short)(_core.Version - 1);

        _timer1 = new(
            state => TimersCallback(state, NatsMsgCarrierTermination.Timeout),
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _timer2 = new(
            state => TimersCallback(state, NatsMsgCarrierTermination.StartUpTimeout),
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _timer3 = new(
            state => TimersCallback(state, NatsMsgCarrierTermination.IdleTimeout),
            this,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public ref MsgWrapper? NextNode => ref _next;

    public void Initialize(
        INatsSerializer? serializer,
        Type? type,
        TimeSpan? timeout,
        TimeSpan? startUpTimeout,
        TimeSpan? idleTimeout,
        int maxMsgCount,
        CancellationToken cancellationToken)
    {
        lock (this)
        {
            Debug.Assert(_timerVersion != _core.Version, "Initialized before reset");
            _timerVersion = _core.Version;

            _serializer = serializer;
            _type = type;
            _ctr = cancellationToken.UnsafeRegister(
                static (msgWrapper, cancellationToken) => ((MsgWrapper)msgWrapper!).Cancel(cancellationToken), this);

            _timeout1 = timeout;
            _timeout2 = startUpTimeout;
            _timeout3 = idleTimeout;
            _msgCount = maxMsgCount;

            if (_timeout1 != null)
            {
                _timer1.Change(_timeout1.Value, Timeout.InfiniteTimeSpan);
            }

            if (_timeout2 != null)
            {
                _timer2.Change(_timeout2.Value, Timeout.InfiniteTimeSpan);
            }

            // Start idle timer only if start-up timer isn't set
            // in case we're allowed to wait longer for the first message.
            if (_timeout2 == null && _timeout3 != null)
            {
                _timer3.Change(_timeout3.Value, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void MsgReceived(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer, NatsConnection connection)
    {
        Monitor.Enter(this);
        try
        {
            if (_isSet) return;
            _isSet = true;

            if (_maxMsgCount > 0 && ++_msgCount > _maxMsgCount)
            {
                SetTerminationResult(this, NatsMsgCarrierTermination.MaxMsgCount);
                return;
            }

            if (_timeout2 != null)
            {
                // Stop Start-up timer on first message
                _timeout2 = null;
                _timer2.Change(Timeout.Infinite, Timeout.Infinite);
            }

            if (_timeout3 != null)
            {
                // Restart Idle timer on every message
                _timer3.Change(_timeout3.Value, Timeout.InfiniteTimeSpan);
            }

            NatsHeaders? headers = null;
            if (headersBuffer != null)
            {
                headers = new NatsHeaders();
                if (!connection.HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
                {
                    throw new NatsException("Error parsing headers");
                }

                headers.SetReadOnly();
            }

            object? modelData = default;
            ReadOnlyMemory<byte>? bufferData = default;

            if (_serializer == null)
            {
                bufferData = payloadBuffer.ToArray();
            }
            else
            {
                if (_type == null)
                {
                    throw new NullReferenceException("Type must be set for serializer");
                }

                modelData = _serializer.Deserialize(payloadBuffer, _type);
            }

            _core.SetResult(new NatsMsgCarrier(
                Subject: subject,
                ReplyTo: replyTo,
                Headers: headers,
                Connection: connection,
                Termination: NatsMsgCarrierTermination.None,
                ModelData: modelData,
                BufferData: bufferData));
        }
        catch (Exception e)
        {
            _core.SetException(e);
        }
        finally
        {
            Monitor.Exit(this);
        }
    }

    public ValueTask<NatsMsgCarrier> MsgRetrieveAsync() => new(this, _core.Version);

    public NatsMsgCarrier GetResult(short token)
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
                _isSet = default;
                _msgCount = default;
                _maxMsgCount = default;

                // Note that _timerVersion is reset in initializer
                _timer1.Change(Timeout.Infinite, Timeout.Infinite);
                _timer2.Change(Timeout.Infinite, Timeout.Infinite);
                _timer3.Change(Timeout.Infinite, Timeout.Infinite);
                _timeout1 = default;
                _timeout2 = default;
                _timeout3 = default;
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

    private static void TimersCallback(object? state, NatsMsgCarrierTermination termination)
    {
        if (state == null) return;
        var self = (MsgWrapper)state;
        lock (self)
        {
            // Guard against timers misfiring after reset
            if (self._core.Version != self._timerVersion) return;

            if (self._isSet) return;
            self._isSet = true;

            SetTerminationResult(self, termination);
        }
    }

    private static void SetTerminationResult(MsgWrapper self, NatsMsgCarrierTermination termination) =>
        self._core.SetResult(new NatsMsgCarrier(
            Termination: termination,
            Subject: string.Empty,
            ReplyTo: default,
            Headers: default,
            Connection: default,
            ModelData: default,
            BufferData: default));

    private void Cancel(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            _core.SetException(ExceptionDispatchInfo.SetCurrentStackTrace(new OperationCanceledException(cancellationToken)));
        }
    }
}
