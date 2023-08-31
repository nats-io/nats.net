using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSSubFetch<TMsg> : NatsSubBase, INatsJSSubFetch<TMsg>
{
    private readonly ILogger _logger;
    private readonly Channel<NatsJSMsg<TMsg?>> _userMsgs;
    private readonly Channel<NatsJSNotification> _notifications;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly Action<NatsJSNotification>? _errorHandler;
    private readonly INatsSerializer _serializer;
    private readonly Timer _hbTimer;
    private readonly Timer _expiresTimer;
    private readonly Task _notificationsTask;

    private readonly long _maxMsgs;
    private readonly long _maxBytes;
    private readonly long _expires;
    private readonly long _idle;
    private readonly long _hbTimeout;

    private long _pendingMsgs;
    private long _pendingBytes;

    public NatsJSSubFetch(
        long maxMsgs,
        long maxBytes,
        TimeSpan expires,
        TimeSpan idle,
        NatsJSContext context,
        string stream,
        string consumer,
        string subject,
        NatsSubOpts? opts,
        Action<NatsJSNotification>? errorHandler)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, opts)
    {
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSSubConsume<TMsg>>();
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _errorHandler = errorHandler;
        _serializer = opts?.Serializer ?? context.Connection.Opts.Serializer;

        _maxMsgs = maxMsgs;
        _maxBytes = maxBytes;
        _expires = expires.ToNanos();
        _idle = idle.ToNanos();
        _hbTimeout = (int)(idle * 2).TotalMilliseconds;
        _pendingMsgs = _maxMsgs;
        _pendingBytes = _maxBytes;

        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg?>>(NatsSub.GetChannelOpts(opts?.ChannelOpts));
        Msgs = _userMsgs.Reader;

        _notifications = Channel.CreateBounded<NatsJSNotification>(NatsSub.GetChannelOpts(opts?.ChannelOpts));
        _notificationsTask = Task.Run(NotificationsLoop);

        _hbTimer = new Timer(
            static state =>
            {
                var self = (NatsJSSubFetch<TMsg>)state!;
                self._notifications.Writer.TryWrite(NatsJSNotification.HeartbeatTimeout);
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _expiresTimer = new Timer(
            static state =>
            {
                var self = (NatsJSSubFetch<TMsg>)state!;
                self.EndSubscription(NatsSubEndReason.Timeout);
            },
            this,
            expires + TimeSpan.FromSeconds(5),
            Timeout.InfiniteTimeSpan);
    }

    public ChannelReader<NatsJSMsg<TMsg?>> Msgs { get; }

    public ValueTask CallMsgNextAsync(ConsumerGetnextRequest request, CancellationToken cancellationToken = default) =>
        Connection.PubModelAsync(
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: NatsJsonSerializer.Default,
            replyTo: Subject,
            headers: default,
            cancellationToken);

    public void ResetHeartbeatTimer() => _hbTimer.Change(_hbTimeout, Timeout.Infinite);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _notificationsTask.ConfigureAwait(false);
        await _hbTimer.DisposeAsync().ConfigureAwait(false);
        await _expiresTimer.DisposeAsync().ConfigureAwait(false);
    }

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        var request = new ConsumerGetnextRequest
        {
            Batch = _maxMsgs,
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
                    if (headers is { Code: 408, Message: NatsHeaders.Messages.RequestTimeout })
                    {
                        EndSubscription(NatsSubEndReason.Timeout);
                    }
                    else if (headers is { Code: 409, Message: NatsHeaders.Messages.MessageSizeExceedsMaxBytes })
                    {
                        EndSubscription(NatsSubEndReason.MaxBytes);
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
            _pendingBytes -= msg.Size;

            await _userMsgs.Writer.WriteAsync(msg).ConfigureAwait(false);
        }

        if (_maxBytes > 0 && _pendingBytes <= 0)
        {
            EndSubscription(NatsSubEndReason.MaxBytes);
        }
        else if (_maxBytes == 0 && _pendingMsgs == 0)
        {
            EndSubscription(NatsSubEndReason.MaxMsgs);
        }
    }

    protected override void TryComplete()
    {
        _userMsgs.Writer.TryComplete();
        _notifications.Writer.TryComplete();
    }

    private async Task NotificationsLoop()
    {
        await foreach (var notification in _notifications.Reader.ReadAllAsync())
        {
            try
            {
                _errorHandler?.Invoke(notification);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Notification error handler error");
            }
        }
    }
}
