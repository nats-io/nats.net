using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSConsume<TMsg> : NatsSubBase, INatsJSConsume<TMsg>
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly Channel<NatsJSMsg<TMsg?>> _userMsgs;
    private readonly Channel<NatsJSNotification> _notifications;
    private readonly Channel<ConsumerGetnextRequest> _pullRequests;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly Action<INatsJSConsume, NatsJSNotification>? _errorHandler;
    private readonly INatsSerializer _serializer;
    private readonly Timer _timer;
    private readonly Task _pullTask;
    private readonly Task _notificationsTask;

    private readonly long _maxMsgs;
    private readonly long _expires;
    private readonly long _idle;
    private readonly long _hbTimeout;
    private readonly long _thresholdMsgs;
    private readonly long _maxBytes;
    private readonly long _thresholdBytes;

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
        NatsSubOpts? opts,
        Action<INatsJSConsume, NatsJSNotification>? errorHandler)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, opts)
    {
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSConsume<TMsg>>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _errorHandler = errorHandler;
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
                "Consumer setup {@Config}",
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
                self.Pull(self._maxMsgs, self._maxBytes);
                self.ResetPending();
                self._notifications.Writer.TryWrite(NatsJSNotification.HeartbeatTimeout);
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg?>>(NatsSub.GetChannelOpts(opts?.ChannelOpts));
        Msgs = _userMsgs.Reader;

        _pullRequests = Channel.CreateBounded<ConsumerGetnextRequest>(NatsSub.GetChannelOpts(opts?.ChannelOpts));
        _pullTask = Task.Run(PullLoop);

        _notifications = Channel.CreateBounded<NatsJSNotification>(NatsSub.GetChannelOpts(opts?.ChannelOpts));
        _notificationsTask = Task.Run(NotificationsLoop);
    }

    public ChannelReader<NatsJSMsg<TMsg?>> Msgs { get; }

    public void Stop() => EndSubscription(NatsSubEndReason.None);

    public ValueTask CallMsgNextAsync(ConsumerGetnextRequest request, CancellationToken cancellationToken = default) =>
        Connection.PubModelAsync(
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: NatsJsonSerializer.Default,
            replyTo: Subject,
            headers: default,
            cancellationToken);

    public void ResetPending()
    {
        _pendingMsgs = _maxMsgs;
        _pendingBytes = _maxBytes;
    }

    public void ResetHeartbeatTimer() => _timer.Change(_hbTimeout, Timeout.Infinite);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _pullTask.ConfigureAwait(false);
        await _notificationsTask.ConfigureAwait(false);
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
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            replyTo: Subject,
            headers: default,
            value: request,
            serializer: NatsJsonSerializer.Default,
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
                            if (_debug)
                            {
                                _logger.LogDebug("Header pending messages current {Pending}", _pendingMsgs);
                            }

                            _pendingMsgs -= pendingMsgs;
                            if (_pendingMsgs < 0)
                                _pendingMsgs = 0;

                            if (_debug)
                            {
                                _logger.LogDebug("Header pending messages {Header} {Pending}", natsPendingMsgs, _pendingMsgs);
                            }
                        }
                        else
                        {
                            _logger.LogError("Can't parse Nats-Pending-Messages {Header}", natsPendingMsgs);
                        }
                    }

                    if (_maxBytes > 0 && headers.TryGetValue("Nats-Pending-Bytes", out var natsPendingBytes))
                    {
                        if (long.TryParse(natsPendingBytes, out var pendingBytes))
                        {
                            if (_debug)
                            {
                                _logger.LogDebug("Header pending bytes current {Pending}", _pendingBytes);
                            }

                            _pendingBytes -= pendingBytes;
                            if (_pendingBytes < 0)
                                _pendingBytes = 0;

                            if (_debug)
                            {
                                _logger.LogDebug("Header pending bytes {Header} {Pending}", natsPendingBytes, _pendingBytes);
                            }
                        }
                        else
                        {
                            _logger.LogError("Can't parse Nats-Pending-Bytes {Header}", natsPendingBytes);
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
                    else
                    {
                        _notifications.Writer.TryWrite(new NatsJSNotification(headers.Code, headers.MessageText));
                    }
                }
                else
                {
                    _logger.LogError(
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

            _pendingMsgs--;

            if (_maxBytes > 0)
            {
                if (_debug)
                    _logger.LogDebug("Message size {Size}", msg.Size);

                _pendingBytes -= msg.Size;
            }

            await _userMsgs.Writer.WriteAsync(msg).ConfigureAwait(false);
        }

        CheckPending();
    }

    protected override void TryComplete()
    {
        _pullRequests.Writer.TryComplete();
        _userMsgs.Writer.TryComplete();
        _notifications.Writer.TryComplete();
    }

    private void CheckPending()
    {
        if (_maxBytes > 0 && _pendingBytes <= _thresholdBytes)
        {
            if (_debug)
                _logger.LogDebug("Check pending bytes {Pending}", _pendingBytes);

            Pull(_maxMsgs, _maxBytes - _pendingBytes);
            ResetPending();
        }
        else if (_maxBytes == 0 && _pendingMsgs <= _thresholdMsgs)
        {
            if (_debug)
                _logger.LogDebug("Check pending messages {Pending}", _pendingMsgs);

            Pull(_maxMsgs - _pendingMsgs, 0);
            ResetPending();
        }
    }

    private void Pull(long batch, long maxBytes) => _pullRequests.Writer.TryWrite(new ConsumerGetnextRequest
    {
        Batch = batch,
        MaxBytes = maxBytes,
        IdleHeartbeat = _idle,
        Expires = _expires,
    });

    private async Task PullLoop()
    {
        await foreach (var pr in _pullRequests.Reader.ReadAllAsync())
        {
            await CallMsgNextAsync(pr).ConfigureAwait(false);
        }
    }

    private async Task NotificationsLoop()
    {
        await foreach (var notification in _notifications.Reader.ReadAllAsync())
        {
            try
            {
                _errorHandler?.Invoke(this, notification);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Notification error handler error");
            }
        }
    }
}
