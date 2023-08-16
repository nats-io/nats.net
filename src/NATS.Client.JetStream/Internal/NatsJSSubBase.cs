using System.Buffers;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal abstract class NatsJSSubBase<T> : NatsSubBase
{
    private readonly bool _trace;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly NatsJSContext _context;
    private readonly NatsJSSubState _state;
    private readonly INatsSerializer _serializer;
    private readonly CancellationToken _cancellationToken;
    private readonly Timer _heartbeatTimer;
    private readonly TimeSpan _hearthBeatTimeout;

    internal NatsJSSubBase(
        string stream,
        string consumer,
        NatsJSContext context,
        ISubscriptionManager manager,
        string subject,
        NatsSubOpts? opts,
        NatsJSSubState state,
        INatsSerializer serializer,
        CancellationToken cancellationToken = default)
        : base(context.Nats, manager, subject, opts)
    {
        _stream = stream;
        _consumer = consumer;
        _context = context;
        _state = state;
        _serializer = serializer;
        _cancellationToken = cancellationToken;
        Logger = Connection.Options.LoggerFactory.CreateLogger<NatsJSSubBase<T>>();
        _trace = Logger.IsEnabled(LogLevel.Trace);

        _hearthBeatTimeout = state.HearthBeatTimeout;

        // TODO: Heartbeat timeouts are signaled through the subscription internal channel
        // so that state transitions can be done in the same loop as other messages
        // to ensure state consistency.
        _heartbeatTimer = new Timer(
            callback: _ => HeartbeatTimerCallback(),
            state: default,
            dueTime: Timeout.Infinite,
            period: Timeout.Infinite);
    }

    protected ILogger Logger { get; }

    public override async ValueTask ReadyAsync()
    {
        await base.ReadyAsync().ConfigureAwait(false);
        await CallMsgNextAsync(_state.GetRequest(init: true)).ConfigureAwait(false);
        ResetHeartbeatTimer();
    }

    protected override async ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        ResetHeartbeatTimer();

        // State is handled in a single-threaded fashion to make sure all decisions
        // are made in order e.g. control messages may change pending counts which are
        // also effected by user messages.
        if (subject == Subject)
        {
            var msg = NatsMsg.Build(
                subject,
                replyTo,
                headersBuffer,
                payloadBuffer,
                Connection,
                Connection.HeaderParser);

            NatsJSNotification? notification = null;

            if (msg.Headers is { } headers)
            {
                if (_trace)
                {
                    Logger.LogTrace("Control message received {Code} {Message}", headers.Code, headers.Message);
                }

                // Read the values of Nats-Pending-Messages and Nats-Pending-Bytes headers.
                // Subtract the values from pending messages count and pending bytes count respectively.                    if (msg.JSMsg.Msg.Headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat } headers)
                if (headers.TryGetValue("Nats-Pending-Messages", out var pendingMsgsStr)
                    && long.TryParse(pendingMsgsStr.ToString(), out var pendingMsgs))
                {
                    _state.ReceivedPendingMsgs(pendingMsgs);
                }

                if (headers.TryGetValue("Nats-Pending-Bytes", out var pendingBytesStr)
                    && long.TryParse(pendingBytesStr.ToString(), out var pendingBytes))
                {
                    _state.ReceivedPendingBytes(pendingBytes);
                }

                // React on other headers
                if (headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat })
                {
                    // Do nothing. Timer is reset for every message already.
                }

                // Stop
                else if (headers is { Code: 409, Message: NatsHeaders.Messages.ConsumerDeleted }
                         or { Code: 409, Message: NatsHeaders.Messages.ConsumerIsPushBased })
                {
                    SetException(new NatsJSConsumerPullTerminated(headers.Code, headers.MessageText));
                }

                // Errors
                else if (headers is { Code: 400, Message: NatsHeaders.Messages.BadRequest })
                {
                    notification = new NatsJSNotification(headers.Code, headers.MessageText);
                }

                // Warnings
                else if (headers is { Code: 409 }
                         && (headers.MessageText.StartsWith("Exceeded MaxRequestBatch of")
                             || headers.MessageText.StartsWith("Exceeded MaxRequestExpires of ")
                             || headers.MessageText.StartsWith("Exceeded MaxRequestMaxBytes of ")
                             || headers.MessageText == "Exceeded MaxWaiting"))
                {
                    notification = new NatsJSNotification(headers.Code, headers.MessageText);
                }

                // Not Telegraphed
                else if (headers is { Code: 404, Message: NatsHeaders.Messages.NoMessages }
                         or { Code: 408, Message: NatsHeaders.Messages.RequestTimeout }
                         or { Code: 409, Message: NatsHeaders.Messages.MessageSizeExceedsMaxBytes })
                {
                    _state.MarkPullAsTerminated();
                }
                else
                {
                    notification = new NatsJSNotification(headers.Code, headers.MessageText);
                    Logger.LogError("Unhandled control message {Code} {Text}", headers.Code, headers.MessageText);
                }
            }
            else
            {
                notification = new NatsJSNotification(999, "Unknown");
            }

            if (notification != null)
                await ReceivedControlMsg(notification);
        }
        else
        {
            try
            {
                var msg = NatsMsg<T?>.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    _serializer);

                _state.MsgReceived(msg.Size);

                if (_state.CanFetch())
                {
                    await CallMsgNextAsync(_state.GetRequest());
                }

                await ReceivedUserMsg(msg).ConfigureAwait(false);

                DecrementMaxMsgs();
            }
            catch (Exception e)
            {
                var payload = new Memory<byte>(new byte[payloadBuffer.Length]);
                payloadBuffer.CopyTo(payload.Span);

                Memory<byte> headers = default;
                if (headersBuffer != null)
                {
                    headers = new Memory<byte>(new byte[headersBuffer.Value.Length]);
                }

                SetException(new NatsSubException($"Message error: {e.Message}", ExceptionDispatchInfo.Capture(e), payload, headers));
            }
        }
    }

    protected abstract void HeartbeatTimerCallback();

    protected abstract ValueTask ReceivedControlMsg(NatsJSNotification notification);

    protected abstract ValueTask ReceivedUserMsg(NatsMsg<T?> msg);

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        yield return PublishCommand<ConsumerGetnextRequest>.Create(
            pool: Connection.ObjectPool,
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            replyTo: Subject,
            headers: default,
            value: _state.GetRequest(init: true),
            serializer: JsonNatsSerializer.Default,
            cancellationToken: default);
    }

    private void ResetHeartbeatTimer() => _heartbeatTimer.Change(_hearthBeatTimeout, Timeout.InfiniteTimeSpan);

    private ValueTask CallMsgNextAsync(ConsumerGetnextRequest request)
    {
        _state.IncrementTotalRequests();
        return Connection.PubModelAsync(
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: Subject,
            headers: default,
            _cancellationToken);
    }
}

internal class NatsJSSubState
{
    private const int LargeMsgsBatchSize = 1_000_000;

    private readonly long _optsMaxBytes;
    private readonly long _optsMaxMsgs;
    private readonly long _optsThresholdMsgs;
    private readonly long _optsThresholdBytes;
    private readonly long _optsIdleHeartbeatNanos;
    private readonly long _optsExpiresNanos;

    private long _pendingMsgs;
    private long _pendingBytes;
    private long _totalRequests;
    private bool _pullTerminated;

    public NatsJSSubState(
        NatsJSOpts? opts = default,
        long? optsMaxBytes = default,
        long? optsMaxMsgs = default,
        long? optsThresholdMsgs = default,
        long? optsThresholdBytes = default,
        TimeSpan? optsIdleHeartbeat = default,
        TimeSpan? optsExpires = default)
    {
        var m = NatsJSOpsDefaults.SetMax(opts, optsMaxMsgs, optsMaxBytes, optsThresholdMsgs, optsThresholdBytes);
        var t = NatsJSOpsDefaults.SetTimeouts(optsExpires, optsIdleHeartbeat);

        _optsMaxBytes = m.MaxBytes;
        _optsMaxMsgs = m.MaxMsgs;
        _optsThresholdMsgs = m.ThresholdMsgs;
        _optsThresholdBytes = m.ThresholdBytes;
        _optsIdleHeartbeatNanos = t.IdleHeartbeat.ToNanos();
        _optsExpiresNanos = t.Expires.ToNanos();
        HearthBeatTimeout = t.IdleHeartbeat * 2;
    }

    public TimeSpan HearthBeatTimeout { get; }

    public void ReceivedPendingMsgs(long pendingMsgs) => _pendingMsgs -= pendingMsgs;

    public void ReceivedPendingBytes(long pendingBytes) => _pendingBytes -= pendingBytes;

    public void MarkPullAsTerminated() => _pullTerminated = true;

    public void IncrementTotalRequests() => Interlocked.Increment(ref _totalRequests);

    public ConsumerGetnextRequest GetRequest(bool init = false)
    {
        _pullTerminated = false;
        _totalRequests++;

        long batch;
        long maxBytes;
        if (init)
        {
            batch = _optsMaxBytes > 0 ? LargeMsgsBatchSize : _optsMaxMsgs;
            maxBytes = _optsMaxBytes > 0 ? _optsMaxBytes : 0;
        }
        else
        {
            batch = _optsMaxBytes > 0 ? LargeMsgsBatchSize : _optsMaxMsgs - _pendingMsgs;
            maxBytes = _optsMaxBytes > 0 ? _optsMaxBytes - _pendingBytes : 0;
        }

        var request = new ConsumerGetnextRequest
        {
            Batch = batch,
            MaxBytes = maxBytes,
            IdleHeartbeat = _optsIdleHeartbeatNanos,
            Expires = _optsExpiresNanos,
            NoWait = false,
            // TODO: Check pull logic
        };

        _pendingMsgs += request.Batch;
        _pendingBytes += request.MaxBytes;

        return request;
    }

    public void MsgReceived(int size)
    {
        _pendingMsgs--;
        _pendingBytes -= size;
    }

    public bool CanFetch() =>
        _pullTerminated
        || _optsThresholdMsgs >= _pendingMsgs
        || (_optsThresholdBytes > 0 && _optsThresholdBytes >= _pendingBytes);
}
