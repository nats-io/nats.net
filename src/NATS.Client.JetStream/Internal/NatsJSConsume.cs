using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal struct PullRequest
{
    public ConsumerGetnextRequest Request { get; init; }

    public string Origin { get; init; }
}

internal class NatsJSConsume<TMsg> : NatsSubBase
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly Channel<NatsJSMsg<TMsg>> _userMsgs;
    private readonly Channel<PullRequest> _pullRequests;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly CancellationToken _cancellationToken;
    private readonly INatsDeserialize<TMsg> _serializer;
    private readonly Timer _timer;
    private readonly Task _pullTask;
    private readonly NatsJSNotificationChannel? _notificationChannel;

    private readonly long _maxMsgs;
    private readonly TimeSpan _expires;
    private readonly TimeSpan _idle;
    private readonly long _hbTimeout;
    private readonly long _thresholdMsgs;
    private readonly long _maxBytes;
    private readonly long _thresholdBytes;

    private readonly object _pendingGate = new();
    private long _pendingMsgs;
    private long _pendingBytes;
    private int _disposed;

    public NatsJSConsume(
        long maxMsgs,
        long thresholdMsgs,
        long maxBytes,
        long thresholdBytes,
        TimeSpan expires,
        TimeSpan idle,
        NatsJSContext context,
        string stream,
        string consumer,
        string subject,
        string? queueGroup,
        Func<INatsJSNotification, CancellationToken, Task>? notificationHandler,
        INatsDeserialize<TMsg> serializer,
        NatsSubOpts? opts,
        CancellationToken cancellationToken)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, queueGroup, opts)
    {
        _cancellationToken = cancellationToken;
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSConsume<TMsg>>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _serializer = serializer;

        if (notificationHandler is { } handler)
        {
            _notificationChannel = new NatsJSNotificationChannel(handler, e => _userMsgs?.Writer.TryComplete(e), cancellationToken);
        }

        _maxMsgs = maxMsgs;
        _thresholdMsgs = thresholdMsgs;
        _maxBytes = maxBytes;
        _thresholdBytes = thresholdBytes;
        _expires = expires;
        _idle = idle;
        _hbTimeout = (int)(idle * 2).TotalMilliseconds;

        if (_debug)
        {
            _logger.LogDebug(
                NatsJSLogEvents.Config,
                "Consume setup maxMsgs:{MaxMsgs}, thresholdMsgs:{ThresholdMsgs}, maxBytes:{MaxBytes}, thresholdBytes:{ThresholdBytes}, expires:{Expires}, idle:{Idle}, hbTimeout:{HbTimeout}",
                maxMsgs,
                thresholdMsgs,
                maxBytes,
                thresholdBytes,
                expires,
                idle,
                _hbTimeout);
        }

        _timer = new Timer(
            static state =>
            {
                var self = (NatsJSConsume<TMsg>)state!;
                self._notificationChannel?.Notify(new NatsJSTimeoutNotification());

                if (self._cancellationToken.IsCancellationRequested)
                {
                    // We complete stop here since heartbeat timeout would kick in
                    // when there are no pull requests or messages left in-flight.
                    self.CompleteStop();
                    return;
                }

                if (self.Connection.ConnectionState == NatsConnectionState.Open)
                {
                    self.Pull("heartbeat-timeout", self._maxMsgs, self._maxBytes);
                    self.ResetPending();
                    if (self._debug)
                    {
                        self._logger.LogDebug(
                            NatsJSLogEvents.IdleTimeout,
                            "Idle heartbeat timeout after {Timeout}ns",
                            self._idle);
                    }
                }
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        // This channel is used to pass messages
        // to the user from the subscription channel (which should be set to a
        // sufficiently large value to avoid blocking socket reads in the
        // NATS connection).
        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg>>(1000);
        Msgs = new ActivityEndingJSMsgReader<TMsg>(_userMsgs.Reader);

        // Capacity as 1 is enough here since it's used for signaling only.
        _pullRequests = Channel.CreateBounded<PullRequest>(1);
        _pullTask = Task.Run(PullLoop);

        ResetPending();
    }

    public ChannelReader<NatsJSMsg<TMsg>> Msgs { get; }

    public ValueTask CallMsgNextAsync(string origin, ConsumerGetnextRequest request, CancellationToken cancellationToken = default)
    {
        if (_cancellationToken.IsCancellationRequested)
            return default;

        if (_debug)
        {
            _logger.LogDebug(NatsJSLogEvents.PullRequest, "Sending pull request for {Origin} {Msgs}, {Bytes}", origin, request.Batch, request.MaxBytes);
        }

        return Connection.PublishAsync(
            Telemetry.NatsInternalActivities,
            subject: $"{_context.Opts.Prefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            replyTo: Subject,
            serializer: NatsJSJsonSerializer<ConsumerGetnextRequest>.Default,
            cancellationToken: cancellationToken);
    }

    public void ResetHeartbeatTimer() => _timer.Change(_hbTimeout, _hbTimeout);

    public override async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await base.DisposeAsync().ConfigureAwait(false);
        await _pullTask.ConfigureAwait(false);
        await _timer.DisposeAsync().ConfigureAwait(false);
        if (_notificationChannel != null)
        {
            await _notificationChannel.DisposeAsync();
        }
    }

    internal override async ValueTask WriteReconnectCommandsAsync(CommandWriter commandWriter, int sid)
    {
        await base.WriteReconnectCommandsAsync(commandWriter, sid);
        ResetPending();

        var request = new ConsumerGetnextRequest
        {
            Batch = _maxMsgs,
            MaxBytes = _maxBytes,
            IdleHeartbeat = _idle,
            Expires = _expires,
        };

        if (_cancellationToken.IsCancellationRequested)
            return;

        await commandWriter.PublishAsync(
            subject: $"{_context.Opts.Prefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            value: request,
            headers: default,
            replyTo: Subject,
            serializer: NatsJSJsonSerializer<ConsumerGetnextRequest>.Default,
            cancellationToken: CancellationToken.None);
    }

    protected override async ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        ResetHeartbeatTimer();

        if (subject == Subject)
        {
            if (headersBuffer.HasValue)
            {
                var headers = new NatsHeaders();
                if (Connection.HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
                {
                    if (_maxBytes == 0 && headers.TryGetValue("Nats-Pending-Messages", out var natsPendingMsgs))
                    {
                        if (long.TryParse(natsPendingMsgs, out var pendingMsgs))
                        {
                            lock (_pendingGate)
                            {
                                if (_debug)
                                {
                                    _logger.LogDebug(
                                        NatsJSLogEvents.PendingCount,
                                        "Header pending messages current {Pending}",
                                        _pendingMsgs);
                                }

                                _pendingMsgs -= pendingMsgs;
                                if (_pendingMsgs < 0)
                                    _pendingMsgs = 0;

                                if (_debug)
                                {
                                    _logger.LogDebug(
                                        NatsJSLogEvents.PendingCount,
                                        "Header pending messages {Header} {Pending}",
                                        natsPendingMsgs,
                                        _pendingMsgs);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogError(NatsJSLogEvents.PendingCount, "Can't parse Nats-Pending-Messages {Header}", natsPendingMsgs);
                        }
                    }

                    if (_maxBytes > 0 && headers.TryGetValue("Nats-Pending-Bytes", out var natsPendingBytes))
                    {
                        if (long.TryParse(natsPendingBytes, out var pendingBytes))
                        {
                            lock (_pendingGate)
                            {
                                if (_debug)
                                {
                                    _logger.LogDebug(NatsJSLogEvents.PendingCount, "Header pending bytes current {Pending}", _pendingBytes);
                                }

                                _pendingBytes -= pendingBytes;
                                if (_pendingBytes < 0)
                                    _pendingBytes = 0;

                                if (_debug)
                                {
                                    _logger.LogDebug(NatsJSLogEvents.PendingCount, "Header pending bytes {Header} {Pending}", natsPendingBytes, _pendingBytes);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogError(NatsJSLogEvents.PendingCount, "Can't parse Nats-Pending-Bytes {Header}", natsPendingBytes);
                        }
                    }

                    if (headers is { Code: 408, Message: NatsHeaders.Messages.RequestTimeout })
                    {
                    }
                    else if (headers is { Code: 409, Message: NatsHeaders.Messages.MessageSizeExceedsMaxBytes })
                    {
                    }
                    else if (headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat })
                    {
                    }
                    else if (headers.Code == 409 && string.Equals(headers.MessageText, "Leadership Change", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug(NatsJSLogEvents.LeadershipChange, "Leadership Change");
                        lock (_pendingGate)
                        {
                            _pendingBytes = 0;
                            _pendingMsgs = 0;
                        }
                    }
                    else if (headers.HasTerminalJSError())
                    {
                        _userMsgs.Writer.TryComplete(new NatsJSProtocolException(headers.Code, headers.Message, headers.MessageText));
                        EndSubscription(NatsSubEndReason.JetStreamError);
                    }
                    else
                    {
                        _notificationChannel?.Notify(new NatsJSProtocolNotification("Unhandled protocol message", headers.Code, headers.MessageText));
                        _logger.LogWarning(NatsJSLogEvents.ProtocolMessage, "Unhandled protocol message: {Code} {Description}", headers.Code, headers.MessageText);
                    }
                }
                else
                {
                    _logger.LogError(
                        NatsJSLogEvents.Headers,
                        "Can't parse headers: {HeadersBuffer}",
                        Encoding.ASCII.GetString(headersBuffer.Value.ToArray()));
                    throw new NatsJSException("Can't parse headers");
                }
            }
            else
            {
                throw new NatsJSException("No header found");
            }
        }
        else
        {
            var msg = new NatsJSMsg<TMsg>(
                ParseMsg(
                    Telemetry.NatsActivities,
                    activityName: "js_receive",
                    subject: subject,
                    replyTo: replyTo,
                    headersBuffer,
                    in payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    serializer: _serializer),
                _context);

            lock (_pendingGate)
            {
                if (_pendingMsgs > 0)
                    _pendingMsgs--;
            }

            if (_maxBytes > 0)
            {
                if (_debug)
                    _logger.LogDebug(NatsJSLogEvents.MessageProperty, "Message size {Size}", msg.Size);

                lock (_pendingGate)
                {
                    _pendingBytes -= msg.Size;
                }
            }

            // Stop feeding the user if we are disposed.
            // We need to exit as soon as possible.
            if (Volatile.Read(ref _disposed) == 0)
            {
                // We can't pass cancellation token here because we need to hand
                // the message to the user to be processed. Writer will be completed
                // when the user calls Stop() or when the subscription is closed.
                await _userMsgs.Writer.WriteAsync(msg, CancellationToken.None).ConfigureAwait(false);
            }
        }

        CheckPending();
    }

    protected override void TryComplete()
    {
        _pullRequests.Writer.TryComplete();
        _userMsgs.Writer.TryComplete();
    }

    private void ResetPending()
    {
        lock (_pendingGate)
        {
            _pendingMsgs = _maxMsgs;
            _pendingBytes = _maxBytes;
        }
    }

    private void CheckPending()
    {
        lock (_pendingGate)
        {
            if (_maxBytes > 0 && _pendingBytes <= _thresholdBytes)
            {
                if (_debug)
                    _logger.LogDebug(NatsJSLogEvents.PendingCount, "Check pending bytes {Pending}, {MaxBytes}", _pendingBytes, _maxBytes);

                Pull("chk-bytes", _maxMsgs, _maxBytes - _pendingBytes);
                ResetPending();
            }
            else if (_maxBytes == 0 && _pendingMsgs <= _thresholdMsgs && _pendingMsgs < _maxMsgs)
            {
                if (_debug)
                    _logger.LogDebug(NatsJSLogEvents.PendingCount, "Check pending messages {Pending}, {MaxMsgs}", _pendingMsgs, _maxMsgs);

                Pull("chk-msgs", _maxMsgs - _pendingMsgs, 0);
                ResetPending();
            }
        }
    }

    private void CompleteStop()
    {
        if (_debug)
        {
            _logger.LogDebug(NatsJSLogEvents.Stopping, "No more pull requests or messages in-flight, stopping");
        }

        // Schedule on the thread pool to avoid potential deadlocks.
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                var self = (NatsJSConsume<TMsg>)state!;
                self._userMsgs.Writer.TryComplete();
                self.EndSubscription(NatsSubEndReason.None);
            },
            this);
    }

    private void Pull(string origin, long batch, long maxBytes) => _pullRequests.Writer.TryWrite(new PullRequest
    {
        Request = new ConsumerGetnextRequest
        {
            Batch = batch,
            MaxBytes = maxBytes,
            IdleHeartbeat = _idle,
            Expires = _expires,
        },
        Origin = origin,
    });

    private async Task PullLoop()
    {
        await foreach (var pr in _pullRequests.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var origin = $"pull-loop({pr.Origin})";
            await CallMsgNextAsync(origin, pr.Request, CancellationToken.None).ConfigureAwait(false);
            if (_debug)
            {
                _logger.LogDebug(NatsJSLogEvents.PullRequest, "Pull request issued for {Origin} {Batch}, {MaxBytes}", origin, pr.Request.Batch, pr.Request.MaxBytes);
            }
        }
    }
}
