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

                // A drain is completing the subscription behind a PING/PONG fence. Don't let
                // the heartbeat callback complete the channel (CompleteStop) and skip the
                // fence, which would drop in-flight messages the drain is meant to preserve.
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
                    // Connection has permanently failed, complete the channel with exception
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

        // This channel is used to pass messages to the user from the subscription.
        // Uses connection's channel options (default DropNewest) to avoid blocking socket reads.
        _userMsgs = Channel.CreateBounded<NatsJSMsg<T>>(
            Connection.GetBoundedChannelOpts(opts?.ChannelOpts),
            msg => Connection.OnMessageDropped(this, _userMsgs?.Reader.Count ?? 0, msg.Msg));
        Msgs = new ActivityEndingMsgReader<NatsJSMsg<T>>(_userMsgs.Reader, this);
    }

    public ChannelReader<NatsJSMsg<T>> Msgs { get; }

    public void StopHeartbeatTimer() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void ResetHeartbeatTimer()
    {
        // Once draining, the heartbeat timer stays stopped so it can't re-arm and complete
        // the channel ahead of the drain fence.
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
        // Mark draining first so the heartbeat callback, its re-arm, and CompleteStop
        // defer to the drain fence, then stop the timer so it can't complete the channel
        // during the drain.
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
            // Control message (e.g. idle heartbeat or flow control) from the server.
            if (headersBuffer.HasValue)
            {
                var headers = new NatsHeaders();
                if (Connection.HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
                {
                    if (headers.TryGetValue("Nats-Consumer-Stalled", out var flowControlReplyTo))
                    {
                        // Client is not reading fast enough. Send a flow control reply so the server
                        // continues to send messages.
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
                        // No action is required for idle heartbeat notifications.
                        // This branch is intentionally left empty.
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
            // Actual JetStream message delivered by the push consumer.
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

            // We can't pass cancellation token here because we need to hand
            // the message to the user to be processed. Writer will be completed
            // when the user calls Stop() or when the subscription is closed.
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

        // Schedule on the thread pool to avoid potential deadlocks.
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var self = (NatsJSPushConsume<T>)state!;

                // If a drain started after this stop was queued, leave completion to the
                // drain fence rather than dropping in-flight messages here.
                if (self._draining)
                    return;

                self._userMsgs.Writer.TryComplete();
                self.EndSubscription(NatsSubEndReason.None);
            },
            this);
    }
}
