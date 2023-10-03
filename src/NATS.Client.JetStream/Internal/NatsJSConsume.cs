using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal struct PullRequest
{
    public ConsumerGetnextRequest Request { get; init; }

    public string Origin { get; init; }
}

internal class NatsJSConsume<TMsg> : NatsSubBase, INatsJSConsume<TMsg>
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly Channel<NatsJSMsg<TMsg?>> _userMsgs;
    private readonly Channel<PullRequest> _pullRequests;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly INatsSerializer _serializer;
    private readonly Timer _timer;
    private readonly Task _pullTask;

    private readonly long _maxMsgs;
    private readonly long _expires;
    private readonly long _idle;
    private readonly long _hbTimeout;
    private readonly long _thresholdMsgs;
    private readonly long _maxBytes;
    private readonly long _thresholdBytes;

    private readonly object _pendingGate = new();
    private long _pendingMsgs;
    private long _pendingBytes;

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
        NatsSubOpts? opts)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, queueGroup, opts)
    {
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSConsume<TMsg>>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _serializer = opts?.Serializer ?? context.Connection.Opts.Serializer;

        _maxMsgs = maxMsgs;
        _thresholdMsgs = thresholdMsgs;
        _maxBytes = maxBytes;
        _thresholdBytes = thresholdBytes;
        _expires = expires.ToNanos();
        _idle = idle.ToNanos();
        _hbTimeout = (int)(idle * 2).TotalMilliseconds;

        if (_debug)
        {
            _logger.LogDebug(
                NatsJSLogEvents.Config,
                "Consume setup {@Config}",
                new
                {
                    maxMsgs,
                    thresholdMsgs,
                    maxBytes,
                    thresholdBytes,
                    expires,
                    idle,
                    _hbTimeout,
                });
        }

        _timer = new Timer(
            static state =>
            {
                var self = (NatsJSConsume<TMsg>)state!;
                self.Pull("heartbeat-timeout", self._maxMsgs, self._maxBytes);
                self.ResetPending();
                if (self._debug)
                {
                    self._logger.LogDebug(
                        NatsJSLogEvents.IdleTimeout,
                        "Idle heartbeat timeout after {Timeout}ns",
                        self._idle);
                }
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg?>>(NatsSub.GetChannelOpts(opts?.ChannelOpts));
        Msgs = _userMsgs.Reader;

        _pullRequests = Channel.CreateBounded<PullRequest>(NatsSub.GetChannelOpts(opts?.ChannelOpts));
        _pullTask = Task.Run(PullLoop);

        ResetPending();
    }

    public ChannelReader<NatsJSMsg<TMsg?>> Msgs { get; }

    public void Stop() => EndSubscription(NatsSubEndReason.None);

    public ValueTask CallMsgNextAsync(string origin, ConsumerGetnextRequest request, CancellationToken cancellationToken = default)
    {
        if (_debug)
        {
            _logger.LogDebug("Sending pull request for {Origin} {Msgs}, {Bytes}", origin, request.Batch, request.MaxBytes);
        }

        return Connection.PubModelAsync(
            subject: $"{_context.Opts.Prefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: NatsJsonSerializer.Default,
            replyTo: Subject,
            headers: default,
            cancellationToken);
    }

    public void ResetHeartbeatTimer() => _timer.Change(_hbTimeout, Timeout.Infinite);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _pullTask.ConfigureAwait(false);
        await _timer.DisposeAsync().ConfigureAwait(false);
    }

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        ResetPending();

        var request = new ConsumerGetnextRequest
        {
            Batch = _maxMsgs,
            MaxBytes = _maxBytes,
            IdleHeartbeat = _idle,
            Expires = _expires,
        };

        yield return PublishCommand<ConsumerGetnextRequest>.Create(
            pool: Connection.ObjectPool,
            subject: $"{_context.Opts.Prefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            replyTo: Subject,
            headers: default,
            value: request,
            serializer: NatsJsonSerializer.Default,
            serializeEarly: true,
            cancellationToken: default);
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
                    else if (headers.HasTerminalJSError())
                    {
                        _userMsgs.Writer.TryComplete(new NatsJSProtocolException($"JetStream server error: {headers.Code} {headers.MessageText}"));
                        EndSubscription(NatsSubEndReason.JetStreamError);
                    }
                    else
                    {
                        if (_debug)
                        {
                            _logger.LogDebug(NatsJSLogEvents.ProtocolMessage, "Protocol message: {Code} {Description}", headers.Code, headers.MessageText);
                        }
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
            var msg = new NatsJSMsg<TMsg?>(
                NatsMsg<TMsg?>.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    _serializer),
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

            await _userMsgs.Writer.WriteAsync(msg).ConfigureAwait(false);
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
        await foreach (var pr in _pullRequests.Reader.ReadAllAsync())
        {
            var origin = $"pull-loop({pr.Origin})";
            await CallMsgNextAsync(origin, pr.Request).ConfigureAwait(false);
            if (_debug)
            {
                _logger.LogDebug(NatsJSLogEvents.PullRequest, "Pull request issued for {Origin} {Batch}, {MaxBytes}", origin, pr.Request.Batch, pr.Request.MaxBytes);
            }
        }
    }
}
