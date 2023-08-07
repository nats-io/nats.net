using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSConsumer
{
    private readonly ILogger _logger;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private volatile bool _deleted;

    public NatsJSConsumer(NatsJSContext context, ConsumerInfo info)
    {
        _context = context;
        Info = info;
        _stream = Info.StreamName;
        _consumer = Info.Name;
        _logger = context.Nats.Options.LoggerFactory.CreateLogger<NatsJSConsumer>();
    }

    public ConsumerInfo Info { get; }

    public async ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _deleted = await _context.DeleteConsumerAsync(_stream, _consumer, cancellationToken);
    }

    /*
     *  Channel Connections
     *  -------------------
     *
     *  - Sub CH:
     *      NatsJSSub message channel where all the inbox messages are
     *      delivered to.
     *
     *  - User Messages CH:
     *      These are all the user messages (i.e. subject != inbox)
     *
     *  - User Notifications CH:
     *      Anything we want to let user know about the state of the
     *      consumer, connection status, timeouts etc.
     *
     *  The main idea is to deliver user and control messages from the server
     *  inbox subscription and internal control messages (e.g. heartbeat
     *  timeouts) to a single 'controller' where all messages would be
     *  processed in order and state managed in one place in a non-concurrent
     *  manner so that races are avoided and it's easier to reason with state
     *  changes.
     *
     *  User Notifications also have their own channel so they can be
     *  prioritized and can run in their own Task where User error handler
     *  will be dispatched.
     *
     *
     *    NATS-SERVER
     *        |                                              User
     *        |             +--> [User Messages CH] -------> message loop
     *        v           /                                  (await foreach)
     *    [Sub CH] ---> Controller (with state)
     *        ^           \                                  User error
     *        |            +--> [User Notifications CH] ---> handler
     *        |                                              (Action<>)
     *        | Internal control msgs
     *        | (e.g. heartbeat timeout)
     *        |
     *  Heartbeat Timer
     *
     */
    public async IAsyncEnumerable<NatsJSMsg<T?>> ConsumeAsync<T>(
        NatsJSConsumeOpts opts,
        NatsSubOpts requestOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        var inbox = $"{_context.Opts.InboxPrefix}.{Guid.NewGuid():n}";

        await using var sub = new NatsJSSub<T>(
            connection: _context.Nats,
            manager: _context.Nats.SubscriptionManager,
            subject: inbox,
            opts: requestOpts,
            serializer: requestOpts.Serializer ?? _context.Nats.Options.Serializer);

        await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            sub: sub,
            cancellationToken);

        // We drop the old message if notification handler isn't able to keep up.
        // This is to avoid blocking the control loop and making sure we deliver all the messages.
        // Assuming newer messages would be more relevant and worth keeping than older ones.
        var notificationChannel = Channel.CreateBounded<NatsJSNotification>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });

        var notifier = Task.Run(async () =>
        {
            await NotificationLoop(opts, notificationChannel, cancellationToken);
        });

        // User messages are buffered here separately to allow smoother flow while control loop
        // pulls more data in the background. This also allows control messages to be dealt with
        // in the same loop as the control messages to keep state updates consistent. This is as
        // opposed to having a control and a message channel at the point of serializing the messages
        // in NatsJSSub class.
        var userMessageChannel = Channel.CreateBounded<NatsJSMsg<T?>>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        var subMsgsChannel = sub.MsgsChannel;

        var controller = Task.Run(async () =>
        {
            await ControlLoop(opts, inbox, subMsgsChannel, userMessageChannel, notificationChannel, cancellationToken);
        });

        while (await userMessageChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (userMessageChannel.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }

        notificationChannel.Writer.Complete();
        await notifier;
        await controller;
    }

    public async ValueTask<NatsJSMsg<T?>> NextAsync<T>(CancellationToken cancellationToken = default)
    {
        await foreach (var natsJSMsg in FetchAsync<T>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: cancellationToken))
        {
            return natsJSMsg;
        }

        throw new NatsJSException("No data");
    }

    public async IAsyncEnumerable<NatsJSMsg<T?>> FetchAsync<T>(
        NatsJSFetchOpts opts,
        NatsSubOpts? requestOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var m = NatsJSOpsDefaults.SetMax(_context.Opts, opts.MaxMsgs, opts.MaxBytes);
        var t = NatsJSOpsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);
        var request = new ConsumerGetnextRequest
        {
            Batch = m.MaxMsgs,
            MaxBytes = m.MaxBytes,
            IdleHeartbeat = t.IdleHeartbeat.ToNanos(),
            Expires = t.Expires.ToNanos(),
        };

        var count = 0;
        var inbox = $"{_context.Opts.InboxPrefix}.{Guid.NewGuid():n}";

        await using var sub = new NatsJSSub<T>(_context.Nats, _context.Nats.SubscriptionManager, inbox, requestOpts, requestOpts?.Serializer ?? _context.Nats.Options.Serializer);
        await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            sub,
            cancellationToken);

        await CallMsgNextAsync(_context, _stream, _consumer, request, inbox, cancellationToken);

        // Avoid additional AsyncEnumerable allocations
        while (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (sub.Msgs.TryRead(out var msg))
            {
                if (msg.IsControlMsg)
                {
                    if (msg.JSMsg.Msg.Headers is { } headers)
                    {
                        if (headers is { Code: 404, Message: NatsHeaders.Messages.NoMessages })
                        {
                            yield break;
                        }

                        if (headers.Code >= 400)
                        {
                            throw new NatsJSConsumerPullTerminated(headers.Code, headers.MessageText);
                        }
                    }

                    // TODO: handle heartbeat etc.
                }
                else
                {
                    yield return msg.JSMsg;

                    if (++count == m.MaxMsgs)
                       yield break;
                }
            }
        }
    }

    private static ValueTask CallMsgNextAsync(NatsJSContext context, string stream, string consumer, ConsumerGetnextRequest request, string inbox, CancellationToken cancellationToken) =>
        context.Nats.PubModelAsync(
            subject: $"$JS.API.CONSUMER.MSG.NEXT.{stream}.{consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: inbox,
            headers: default,
            cancellationToken);

    private async Task NotificationLoop(
        NatsJSConsumeOpts opts,
        Channel<NatsJSNotification, NatsJSNotification> notificationChannel,
        CancellationToken cancellationToken)
    {
        {
            await foreach (var notification in notificationChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    opts.ErrorHandler?.Invoke(notification);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "User notification callback error");
                }
            }
        }
    }

    private async Task ControlLoop<T>(
        NatsJSConsumeOpts opts,
        string inbox,
        Channel<NatsJSControlMsg<T?>> subMsgsChannel,
        Channel<NatsJSMsg<T?>, NatsJSMsg<T?>> userChannel,
        Channel<NatsJSNotification, NatsJSNotification> notificationChannel,
        CancellationToken cancellationToken)
    {
        var timeoutMsg = new NatsJSControlMsg<T?> { ControlMsgType = NatsJSControlMsgType.Timeout };

        var m = NatsJSOpsDefaults.SetMax(_context.Opts, opts.MaxMsgs, opts.MaxBytes, opts.ThresholdMsgs, opts.ThresholdBytes);
        var t = NatsJSOpsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);

        var hearthBeatTimeout = t.IdleHeartbeat * 2;

        // Heartbeat timeouts are signaled through the subscription internal channel
        // so that state transitions can be done in the same loop as other messages
        // to ensure state consistency.
        var heartbeatTimer = new Timer(
            callback: _ =>
            {
                var valueTask = subMsgsChannel.Writer.WriteAsync(timeoutMsg, cancellationToken);
                if (!valueTask.IsCompleted)
                    valueTask.GetAwaiter().GetResult();
            },
            state: default,
            dueTime: Timeout.Infinite,
            period: Timeout.Infinite);

        // State is handled in a single-threaded fashion to make sure all decisions
        // are made in order e.g. control messages may change pending counts which are
        // also effected by user messages.
        await using var state = new State<T>(
            m.MaxMsgs,
            m.MaxBytes,
            t.IdleHeartbeat,
            t.Expires,
            m.ThresholdMsgs,
            m.ThresholdBytes,
            notificationChannel,
            _logger);

        await CallMsgNextAsync(_context, _stream, _consumer, state.GetRequest(), inbox, cancellationToken);

        ResetHeartbeatTimer(heartbeatTimer, hearthBeatTimeout);

        while (await subMsgsChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (subMsgsChannel.Reader.TryRead(out var msg))
            {
                if (msg.ControlMsgType != NatsJSControlMsgType.Timeout)
                {
                    // Except for the internal Timeout message
                    ResetHeartbeatTimer(heartbeatTimer, hearthBeatTimeout);
                }

                if (msg.IsControlMsg)
                {
                    state.WriteControlMsg(msg);
                }
                else
                {
                    var jsMsg = msg.JSMsg;

                    state.MsgReceived(jsMsg.Msg.Size);

                    await userChannel.Writer.WriteAsync(msg.JSMsg, cancellationToken);

                    if (state.CanFetch())
                    {
                        await CallMsgNextAsync(_context, _stream, _consumer, state.GetRequest(), inbox, cancellationToken);
                    }
                }
            }
        }

        await heartbeatTimer.DisposeAsync();

        return;

        static void ResetHeartbeatTimer(Timer timer, TimeSpan timeSpan)
        {
            timer.Change(timeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }

    internal class State<T> : IAsyncDisposable
    {
        private const int LargeMsgsBatchSize = 1_000_000;

        private readonly ILogger _logger;
        private readonly bool _trace;
        private readonly long _optsMaxBytes;
        private readonly long _optsMaxMsgs;
        private readonly long _optsIdleHeartbeatNanos;
        private readonly long _optsExpiresNanos;
        private readonly long _optsThresholdMsgs;
        private readonly long _optsThresholdBytes;
        private readonly Channel<NatsJSNotification, NatsJSNotification> _notificationChannel;

        private long _pendingMsgs;
        private long _pendingBytes;
        private long _totalRequests;
        private bool _pullTerminated;

        public State(
            int optsMaxMsgs,
            int optsMaxBytes,
            TimeSpan optsIdleHeartbeat,
            TimeSpan optsExpires,
            int optsThresholdMsgs,
            int optsThresholdBytes,
            Channel<NatsJSNotification, NatsJSNotification> notificationChannel,
            ILogger logger)
        {
            _notificationChannel = notificationChannel;
            _logger = logger;
            _trace = logger.IsEnabled(LogLevel.Trace);
            _optsMaxBytes = optsMaxBytes;
            _optsMaxMsgs = optsMaxMsgs;
            _optsIdleHeartbeatNanos = optsIdleHeartbeat.ToNanos();
            _optsExpiresNanos = optsExpires.ToNanos();
            _optsThresholdMsgs = optsThresholdMsgs;
            _optsThresholdBytes = optsThresholdBytes;
        }

        public ConsumerGetnextRequest GetRequest()
        {
            _pullTerminated = false;
            _totalRequests++;
            var request = new ConsumerGetnextRequest
            {
                Batch = _optsMaxBytes > 0 ? LargeMsgsBatchSize : _optsMaxMsgs - _pendingMsgs,
                MaxBytes = _optsMaxBytes > 0 ? _optsMaxBytes - _pendingBytes : 0,
                IdleHeartbeat = _optsIdleHeartbeatNanos,
                Expires = _optsExpiresNanos,
                NoWait = true,
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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void WriteControlMsg(NatsJSControlMsg<T?> msg)
        {
            if (msg.ControlMsgType == NatsJSControlMsgType.Timeout)
            {
                // Timeout is an internal message. Handle here and there is no
                // need to check for anything else.
                NotifyUser(NatsJSNotification.Timeout);
            }
            else
            {
                if (msg.JSMsg.Msg.Headers is { } headers)
                {
                    if (_trace)
                    {
                        _logger.LogTrace("Control message received {Code} {Message}", headers.Code, headers.Message);
                    }

                    // Read the values of Nats-Pending-Messages and Nats-Pending-Bytes headers.
                    // Subtract the values from pending messages count and pending bytes count respectively.                    if (msg.JSMsg.Msg.Headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat } headers)
                    if (headers.TryGetValue("Nats-Pending-Messages", out var pendingMsgsStr)
                        && long.TryParse(pendingMsgsStr.ToString(), out var pendingMsgs))
                    {
                        _pendingMsgs -= pendingMsgs;
                    }

                    if (headers.TryGetValue("Nats-Pending-Bytes", out var pendingBytesStr)
                        && long.TryParse(pendingBytesStr.ToString(), out var pendingBytes))
                    {
                        _pendingBytes -= pendingBytes;
                    }

                    // React on other headers
                    if (headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat })
                    {
                        // Do nothing. Timer is reset for every message already.
                    }

                    // Stop
                    else if (headers is { Code: 409, Message: NatsHeaders.Messages.ConsumerDeleted })
                    {
                        throw new NatsJSConsumerPullTerminated(headers.Code, headers.MessageText);
                    }

                    // Errors
                    else if (headers is { Code: 400, Message: NatsHeaders.Messages.BadRequest }
                             or { Code: 409, Message: NatsHeaders.Messages.ConsumerIsPushBased })
                    {
                        NotifyUser(new NatsJSNotification(headers.Code, headers.MessageText));
                    }

                    // Warnings
                    else if (headers is { Code: 409 }
                             && (headers.MessageText.StartsWith("Exceeded MaxRequestBatch of")
                                 || headers.MessageText.StartsWith("Exceeded MaxRequestExpires of ")
                                 || headers.MessageText.StartsWith("Exceeded MaxRequestMaxBytes of ")
                                 || headers.MessageText == "Exceeded MaxWaiting"))
                    {
                        NotifyUser(new NatsJSNotification(headers.Code, headers.MessageText));
                    }

                    // Not Telegraphed
                    else if (headers is { Code: 404, Message: NatsHeaders.Messages.NoMessages }
                             or { Code: 408, Message: NatsHeaders.Messages.RequestTimeout }
                             or { Code: 409, Message: NatsHeaders.Messages.MessageSizeExceedsMaxBytes })
                    {
                        _pullTerminated = true;
                    }
                    else
                    {
                        NotifyUser(new NatsJSNotification(headers.Code, headers.MessageText));
                        _logger.LogError("Unhandled control message {ControlMsgType}", msg.ControlMsgType);
                    }
                }
            }
        }

        private void NotifyUser(NatsJSNotification notification) => _notificationChannel.Writer.TryWrite(notification);
    }
}

public class NatsJSNotification
{
    public static readonly NatsJSNotification Timeout = new(code: 100, message: "Timeout");

    public NatsJSNotification(int code, string message)
    {
        Code = code;
        Message = message;
    }

    public int Code { get; }

    public string Message { get; }
}
