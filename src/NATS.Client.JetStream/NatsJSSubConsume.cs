using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSSubConsume<TMsg> : NatsSubBase, INatsJSSubConsume<TMsg>
{
    private readonly ILogger _logger;
    private readonly Channel<NatsJSMsg<TMsg?>> _userMsgs;
    private readonly Channel<NatsJSNotification> _notifications;
    private readonly Channel<ConsumerGetnextRequest> _pullRequests;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly Action<NatsJSNotification>? _errorHandler;
    private readonly INatsSerializer _serializer;
    private readonly Timer _timer;
    private readonly Task _pullTask;
    private readonly Task _notificationsTask;

    private readonly long _batch;
    private readonly long _expires;
    private readonly long _idle;
    private readonly long _hbTimeout;
    private readonly long _threshold;

    private long _pending;

    public NatsJSSubConsume(
        long batch,
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

        _batch = batch;
        _threshold = batch / 2;
        _expires = expires.ToNanos();
        _idle = idle.ToNanos();
        _hbTimeout = (int)(idle * 2).TotalMilliseconds;

        _timer = new Timer(
            state =>
            {
                var self = (NatsJSSubConsume<TMsg>)state!;
                self.Pull(_batch);
                ResetPending();
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg?>>(NatsSub.GetChannelOptions(opts?.ChannelOptions));
        Msgs = _userMsgs.Reader;

        _pullRequests = Channel.CreateBounded<ConsumerGetnextRequest>(NatsSub.GetChannelOptions(opts?.ChannelOptions));
        _pullTask = Task.Run(PullLoop);

        _notifications = Channel.CreateBounded<NatsJSNotification>(NatsSub.GetChannelOptions(opts?.ChannelOptions));
        _notificationsTask = Task.Run(NotificationsLoop);
    }

    public ChannelReader<NatsJSMsg<TMsg?>> Msgs { get; }

    public ValueTask CallMsgNextAsync(ConsumerGetnextRequest request, CancellationToken cancellationToken = default) =>
        Connection.PubModelAsync(
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: Subject,
            headers: default,
            cancellationToken);

    public void ResetPending() => _pending = _batch;

    public void ResetHeartbeatTimer() => _timer.Change(_hbTimeout, Timeout.Infinite);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _pullTask;
        await _notificationsTask;
        await _timer.DisposeAsync();
    }

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        ResetPending();

        var request = new ConsumerGetnextRequest
        {
            Batch = _batch,
            IdleHeartbeat = _idle,
            Expires = _expires,
        };

        yield return PublishCommand<ConsumerGetnextRequest>.Create(
            pool: Connection.ObjectPool,
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            replyTo: Subject,
            headers: default,
            value: request,
            serializer: JsonNatsSerializer.Default,
            cancellationToken: default);
    }

    protected override ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        ResetHeartbeatTimer();
        try
        {
            if (subject == Subject)
            {
                if (headersBuffer.HasValue)
                {
                    var headers = new NatsHeaders();
                    if (Connection.HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
                    {
                        if (headers.TryGetValue("Nats-Pending-Messages", out var natsPendingMsgs))
                        {
                            if (int.TryParse(natsPendingMsgs, out var pendingMsgs))
                            {
                                _pending -= pendingMsgs;
                                if (_pending < 0)
                                    _pending = 0;
                            }
                        }

                        if (headers is { Code: 408, Message: NatsHeaders.Messages.RequestTimeout })
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

                return ValueTask.CompletedTask;
            }
            else
            {
                var msg = new NatsJSMsg<TMsg?>(NatsMsg<TMsg?>.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    _serializer));

                _pending--;

                return _userMsgs.Writer.WriteAsync(msg);
            }
        }
        finally
        {
            CheckPending();
        }
    }

    protected override void TryComplete()
    {
        _pullRequests.Writer.TryComplete();
        _userMsgs.Writer.TryComplete();
        _notifications.Writer.TryComplete();
    }

    private void CheckPending()
    {
        if (_pending <= _threshold)
        {
            Pull(_batch - _pending);
            ResetPending();
        }
    }

    private void Pull(long batch) => _pullRequests.Writer.TryWrite(new ConsumerGetnextRequest
    {
        Batch = batch, IdleHeartbeat = _idle, Expires = _expires,
    });

    private async Task PullLoop()
    {
        await foreach (var pr in _pullRequests.Reader.ReadAllAsync())
        {
            await CallMsgNextAsync(pr);
        }
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
