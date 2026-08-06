using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;

namespace NATS.Client.JetStream.Internal;

/// <summary>
/// Push consumption engine. Subscribes to a deliver subject and yields the messages the
/// server pushes for a push consumer, responding to JetStream control messages (flow
/// control, idle heartbeats, terminal conditions) as they arrive.
/// </summary>
/// <typeparam name="T">Message type to deserialize.</typeparam>
internal class NatsJSPushConsume<T> : NatsSubBase
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly NatsJSContext _context;
    private readonly CancellationToken _cancellationToken;
    private readonly INatsDeserialize<T> _serializer;
    private readonly NatsJSNotificationChannel? _notificationChannel;
    private readonly Channel<NatsJSMsg<T>> _userMsgs;
    private readonly Timer _timer;
    private readonly int _hbTimeout;
    private volatile bool _draining;

    public NatsJSPushConsume(
        NatsJSContext context,
        string subject,
        string? queueGroup,
        TimeSpan idleHeartbeat,
        Func<INatsJSNotification, CancellationToken, Task>? notificationHandler,
        INatsDeserialize<T> serializer,
        NatsSubOpts? opts,
        CancellationToken cancellationToken)
        : base(
            connection: context.Connection,
            manager: context.Connection.SubscriptionManager,
            subject: subject,
            queueGroup: queueGroup,
            opts: opts)
    {
        _cancellationToken = cancellationToken;
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSPushConsume<T>>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _context = context;
        _serializer = serializer;

        if (notificationHandler is { } handler)
        {
            _notificationChannel = new NatsJSNotificationChannel(handler, e => _userMsgs?.Writer.TryComplete(e), cancellationToken);
        }

        _hbTimeout = idleHeartbeat > TimeSpan.Zero ? (int)new TimeSpan(idleHeartbeat.Ticks * 2).TotalMilliseconds : 0;

        _timer = new Timer(
            static state =>
            {
                var self = (NatsJSPushConsume<T>)state!;

                if (self._draining)
                    return;

                if (self.Connection.ConnectionState == NatsConnectionState.Reconnecting)
                    return;

                self._notificationChannel?.Notify(NatsJSTimeoutNotification.Default);

                if (self._cancellationToken.IsCancellationRequested)
                {
                    self.CompleteStop();
                    return;
                }

                if (self.Connection.ConnectionState == NatsConnectionState.Failed)
                {
                    self._userMsgs.Writer.TryComplete(new NatsConnectionFailedException("Connection is in failed state"));
                    self.CompleteStop();
                    return;
                }

                if (self._debug)
                {
                    self._logger.LogDebug(
                        NatsJSLogEvents.IdleTimeout,
                        "Idle heartbeat timeout after {Timeout}ns",
                        self._hbTimeout);
                }
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _userMsgs = Channel.CreateBounded<NatsJSMsg<T>>(
            Connection.GetBoundedChannelOpts(opts?.ChannelOpts),
            msg => Connection.OnMessageDropped(this, _userMsgs?.Reader.Count ?? 0, msg.Msg));
        Msgs = new ActivityEndingMsgReader<NatsJSMsg<T>>(_userMsgs.Reader, this);
    }

    public ChannelReader<NatsJSMsg<T>> Msgs { get; }

    public void StopHeartbeatTimer() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void ResetHeartbeatTimer()
    {
        if (_draining)
            return;

        if (_hbTimeout > 0)
            _timer.Change(_hbTimeout, Timeout.Infinite);
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await DrainOnDisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
#if NETSTANDARD2_0
            _timer.Dispose();
#else
            await _timer.DisposeAsync().ConfigureAwait(false);
#endif
            if (_notificationChannel != null)
            {
                await _notificationChannel.DisposeAsync();
            }
        }
    }

    protected override void StopDelivery()
    {
        _draining = true;
        StopHeartbeatTimer();
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
                    if (headers.TryGetValue("Nats-Consumer-Stalled", out var flowControlReplyTo))
                    {
                        await Connection.PublishAsync(flowControlReplyTo, cancellationToken: _cancellationToken);
                    }

                    if (headers is { Code: 100, MessageText: "FlowControl Request" })
                    {
#pragma warning disable CS0618 // Type or member is obsolete
                        var msg = new NatsJSMsg<T>(
                            NatsMsg<T>.Build(
                                subject,
                                replyTo,
                                headersBuffer,
                                payloadBuffer,
                                Connection,
                                Connection.HeaderParser,
                                _serializer),
                            _context);
                        await msg.ReplyAsync(cancellationToken: _cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
                    }

                    if (headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat })
                    {
                    }
                    else if (headers.HasTerminalJSError())
                    {
                        _userMsgs.Writer.TryComplete(new NatsJSProtocolException(headers.Code, headers.Message, headers.MessageText));
                        EndSubscription(NatsSubEndReason.JetStreamError);
                    }
                    else if (headers.Code != 100 || headers.Message != NatsHeaders.Messages.Text)
                    {
                        _logger.LogWarning(NatsJSLogEvents.ProtocolMessage, "Unhandled control message: {Code} {Description}", headers.Code, headers.MessageText);
                    }
                }
                else
                {
                    _logger.LogError(NatsJSLogEvents.ProtocolMessage, "Can't parse control message headers");
                }
            }
            else
            {
                _logger.LogWarning(NatsJSLogEvents.ProtocolMessage, "Protocol error: No header found in control message");
            }
        }
        else
        {
            var msg = new NatsJSMsg<T>(
                NatsMsg<T>.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    _serializer),
                _context);

            await _userMsgs.Writer.WriteAsync(msg).ConfigureAwait(false);

            ResetSlowConsumer(_userMsgs.Reader.Count);
        }
    }

    protected override void TryComplete()
    {
        _userMsgs.Writer.TryComplete();
    }

    private void CompleteStop()
    {
        if (_debug)
        {
            _logger.LogDebug(NatsJSLogEvents.Stopping, "No more messages in-flight, stopping");
        }

        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var self = (NatsJSPushConsume<T>)state!;

                if (self._draining)
                    return;

                self._userMsgs.Writer.TryComplete();
                self.EndSubscription(NatsSubEndReason.None);
            },
            this);
    }
}
