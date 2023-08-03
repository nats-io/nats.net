using System.ComponentModel;
using System.Diagnostics;
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
    private bool _deleted;

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

    public async IAsyncEnumerable<NatsJSMsg<T?>> ConsumeAsync<T>(NatsJSConsumeOpts opts, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        var requestOpts = default(NatsSubOpts);

        await using var sub = await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            builder: NatsJSSubModelBuilder<T>.For(requestOpts.Serializer ?? _context.Nats.Options.Serializer),
            cancellationToken);

        // We drop the old message if notification handler isn't able to keep up.
        // This is to avoid blocking the control loop and making sure we deliver all the messages.
        // Assuming newer messages would be more relevant and worth keeping than older ones.
        Channel<int> notificationChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
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

        var subMsgs = sub.Msgs;
        var subMsgWriter = sub.MsgWriter;
        var timeoutMsg = new NatsJSControlMsg<T?> { ControlMsgType = NatsJSControlMsgType.Timeout };

        // Heartbeat timeouts are signaled through the subscription internal channel
        // so that state transitions can be done in the same loop as other messages
        // to ensure state consistency.
        // TODO: Having them on the same channel might delay user notifications going out on time.
        Timer heartbeatTimer = new Timer(
            callback: _ =>
            {
                subMsgWriter.WriteAsync(timeoutMsg, cancellationToken).GetAwaiter().GetResult();
            },
            state: default,
            dueTime: Timeout.Infinite,
            period: Timeout.Infinite);

        var notifier = Task.Run(async () =>
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
        });

        var controller = Task.Run(async () =>
        {
            await ControlLoop(opts, cancellationToken, inbox, subMsgs, userMessageChannel, notificationChannel, heartbeatTimer);
        });

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }

        // Deliver user messages as enumerable
        while (await userMessageChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (userMessageChannel.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }

        await heartbeatTimer.DisposeAsync();
        notificationChannel.Writer.Complete();
        await notifier;
        await controller;
    }

    public async ValueTask<NatsJSMsg<T?>> NextAsync<T>(CancellationToken cancellationToken = default)
    {
        await foreach (var natsJSMsg in FetchAsync<T>(1, cancellationToken))
        {
            return natsJSMsg;
        }

        throw new NatsJSException("No data");
    }

    public async IAsyncEnumerable<NatsJSMsg<T?>> FetchAsync<T>(
        int maxMsgs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new ConsumerGetnextRequest { Batch = maxMsgs, };

        var count = 0;
        await foreach (var msg in ConsumeRawAsync<T>(request, default, cancellationToken).ConfigureAwait(false))
        {
            if (msg.IsControlMsg)
            {
                // TODO: control messages
            }
            else
            {
                yield return msg.JSMsg;

                if (++count == maxMsgs)
                    break;
            }
        }
    }

    internal async IAsyncEnumerable<NatsJSControlMsg> ConsumeRawAsync(
        ConsumerGetnextRequest request,
        NatsSubOpts requestOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        await using var sub = await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            builder: NatsJSSubBuilder.Default,
            cancellationToken);

        await _context.Nats.PubModelAsync(
            subject: $"$JS.API.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: inbox,
            headers: default,
            cancellationToken);

        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
        {
            yield return msg;
        }

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }
    }

    internal async IAsyncEnumerable<NatsJSControlMsg<T?>> ConsumeRawAsync<T>(
        ConsumerGetnextRequest request,
        NatsSubOpts requestOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        await using var sub = await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            builder: NatsJSSubModelBuilder<T>.For(requestOpts.Serializer ?? _context.Nats.Options.Serializer),
            cancellationToken);

        await _context.Nats.PubModelAsync(
            subject: $"$JS.API.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: inbox,
            headers: default,
            cancellationToken);

        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
        {
            yield return msg;
        }

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }
    }

    private async Task ControlLoop<T>(
        NatsJSConsumeOpts opts,
        CancellationToken cancellationToken,
        string inbox,
        ChannelReader<NatsJSControlMsg<T?>> subMsgs,
        Channel<NatsJSMsg<T?>> userChannel,
        Channel<int> notificationChannel,
        Timer heartbeatTimer)
    {
        var hearthBeatTimeout = opts.IdleHeartbeat * 2;

        static async ValueTask MsgNextAsync(NatsJSContext context, string stream, string consumer, ConsumerGetnextRequest request, string inbox, CancellationToken cancellationtoken)
        {
            await context.Nats.PubModelAsync(
                subject: $"$JS.API.CONSUMER.MSG.NEXT.{stream}.{consumer}",
                data: request,
                serializer: JsonNatsSerializer.Default,
                replyTo: inbox,
                headers: default,
                cancellationtoken);
        }

        // State is handled in a single-threaded fashion to make sure all decisions
        // are made in order e.g. control messages may change pending counts which are
        // also effected by user messages.
        await using var state = new State<T>(opts, _logger);

        await MsgNextAsync(_context, _stream, _consumer, state.GetRequest(), inbox, cancellationToken);

        while (await subMsgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (subMsgs.TryRead(out var msg))
            {
                if (msg.IsControlMsg)
                {
                    if (msg.ControlMsgType == NatsJSControlMsgType.Timeout)
                    {
                        notificationChannel.Writer.TryWrite(1);
                    }
                    else
                    {
                        heartbeatTimer.Change(hearthBeatTimeout, Timeout.InfiniteTimeSpan);
                        state.WriteControlMsg(msg);
                    }
                }
                else
                {
                    heartbeatTimer.Change(hearthBeatTimeout, Timeout.InfiniteTimeSpan);

                    var jsMsg = msg.JSMsg;

                    state.MsgReceived(jsMsg.Msg.Size);

                    await userChannel.Writer.WriteAsync(msg.JSMsg, cancellationToken);

                    if (state.CanFetch())
                    {
                        await MsgNextAsync(_context, _stream, _consumer, state.GetRequest(), inbox, cancellationToken);
                    }
                }
            }
        }
    }

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }

    internal class State<T> : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly long _optsMaxBytes;
        private readonly long _optsMaxMsgs;
        private readonly TimeSpan _optsIdleHeartbeat;
        private readonly long _optsIdleHeartbeatNanos;
        private readonly long _optsExpiresNanos;
        private readonly long _optsThresholdMsgs;
        private readonly long _optsThresholdBytes;
        private long _pendingMsgs;
        private long _pendingBytes;
        private long _totalRequests;

        public State(NatsJSConsumeOpts opts, ILogger logger)
        {
            _logger = logger;
            _optsMaxBytes = opts.MaxBytes;
            _optsMaxMsgs = opts.MaxMsgs;
            _optsIdleHeartbeat = opts.IdleHeartbeat;
            _optsIdleHeartbeatNanos = (long)(opts.IdleHeartbeat.TotalMilliseconds * 1_000);
            _optsExpiresNanos = (long)(opts.Expires.TotalMilliseconds * 1_000);
            _optsThresholdMsgs = opts.ThresholdMsgs;
            _optsThresholdBytes = opts.ThresholdBytes;
        }

        public ConsumerGetnextRequest GetRequest()
        {
            _totalRequests++;
            var request = new ConsumerGetnextRequest
            {
                Batch = _optsMaxBytes > 0 ? 1_000_000 : _optsMaxMsgs - _pendingMsgs,
                MaxBytes = _optsMaxBytes > 0 ? _optsMaxBytes - _pendingBytes : 0,
                IdleHeartbeat = _optsIdleHeartbeatNanos,
                Expires = _optsExpiresNanos,
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
            _optsThresholdMsgs >= _pendingMsgs || (_optsThresholdBytes > 0 && _optsThresholdBytes >= _pendingBytes);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void WriteControlMsg(NatsJSControlMsg<T?> msg)
        {
            // Read the values of Nats-Pending-Messages and Nats-Pending-Bytes headers.
            // Subtract the values from pending messages count and pending bytes count respectively.                    if (msg.JSMsg.Msg.Headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat } headers)
            if (msg.JSMsg.Msg.Headers is { } headers)
            {
                if (headers.TryGetValue("Nats-Pending-Messages", out var pendingMsgsStr)
                    && long.TryParse(pendingMsgsStr.ToString(), out var pendingMsgs))
                {
                    Interlocked.Add(ref _pendingMsgs, -pendingMsgs);
                }

                if (headers.TryGetValue("Nats-Pending-Bytes", out var pendingBytesStr)
                    && long.TryParse(pendingBytesStr.ToString(), out var pendingBytes))
                {
                    Interlocked.Add(ref _pendingBytes, -pendingBytes);
                }
            }

            if (msg.JSMsg.Msg.Headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat })
            {
                // Do nothing. Timer is reset for every message already.
            }
            else
            {
                _logger.LogError("Unhandled control message {ControlMsgType}", msg.ControlMsgType);
            }
        }
    }
}

public record NatsJSConsumeOpts
{
    private static readonly TimeSpan ExpiresDefault = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpiresMin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatCap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatMin = TimeSpan.FromSeconds(.5);

    public NatsJSConsumeOpts(
        int? maxMsgs = default,
        TimeSpan? expires = default,
        int? maxBytes = default,
        TimeSpan? idleHeartbeat = default,
        int? thresholdMsgs = default,
        int? thresholdBytes = default,
        Action<int>? errorHandler = default)
    {
        if (maxMsgs.HasValue && maxBytes.HasValue)
        {
            throw new NatsJSException($"You can only set {nameof(MaxBytes)} or {nameof(MaxMsgs)}");
        }
        else if (!maxMsgs.HasValue && !maxBytes.HasValue)
        {
            MaxMsgs = 1_000;
            MaxBytes = 0;
        }
        else if (maxMsgs.HasValue && !maxBytes.HasValue)
        {
            MaxMsgs = maxMsgs.Value;
            MaxBytes = 0;
        }
        else if (!maxMsgs.HasValue && maxBytes.HasValue)
        {
            MaxMsgs = 1_000_000;
            MaxBytes = maxBytes.Value;
        }

        Expires = expires ?? ExpiresDefault;
        if (Expires < ExpiresMin)
            Expires = ExpiresMin;

        IdleHeartbeat = idleHeartbeat ?? Expires / 2;
        if (IdleHeartbeat > HeartbeatCap)
            IdleHeartbeat = HeartbeatCap;
        if (IdleHeartbeat < HeartbeatMin)
            IdleHeartbeat = HeartbeatMin;

        ThresholdMsgs = thresholdMsgs ?? MaxMsgs / 2;
        if (ThresholdMsgs > MaxMsgs)
            ThresholdMsgs = MaxMsgs;

        ThresholdBytes = thresholdBytes ?? MaxBytes / 2;
        if (ThresholdBytes > MaxBytes)
            ThresholdBytes = MaxBytes;

        ErrorHandler = errorHandler;
    }

    /// <summary>
    /// Maximum number of messages stored in the buffer
    /// </summary>
    public Action<int>? ErrorHandler { get; }

    /// <summary>
    /// Maximum number of messages stored in the buffer
    /// </summary>
    public long MaxMsgs { get; }

    /// <summary>
    /// Amount of time to wait for a single pull request to expire
    /// </summary>
    public TimeSpan Expires { get; }

    /// <summary>
    /// Maximum number of bytes stored in the buffer
    /// </summary>
    public long MaxBytes { get; }

    /// <summary>
    /// Amount idle time the server should wait before sending a heartbeat
    /// </summary>
    public TimeSpan IdleHeartbeat { get; }

    /// <summary>
    /// Number of messages left in the buffer that should trigger a low watermark on the client, and influence it to request more messages
    /// </summary>
    public long ThresholdMsgs { get; }

    /// <summary>
    /// Hint for the number of bytes left in buffer that should trigger a low watermark on the client, and influence it to request more data.
    /// </summary>
    public long ThresholdBytes { get; }
}

public record NatsJSNextOpts
{
    /// <summary>
    /// Amount of time to wait for the request to expire (in nanoseconds)
    /// </summary>
    public TimeSpan Expires { get; init; }

    /// <summary>
    /// Amount idle time the server should wait before sending a heartbeat. For requests with expires > 30s, heartbeats should be enabled by default
    /// </summary>
    public TimeSpan? IdleHeartbeat { get; init; }
}

public record NatsJSFetchOpts
{
    /// <summary>
    /// Maximum number of messages to return
    /// </summary>
    public int? MaxMessages { get; init; }

    /// <summary>
    /// Amount of time to wait for the request to expire
    /// </summary>
    public TimeSpan Expires { get; init; }

    /// <summary>
    /// Maximum number of bytes to return
    /// </summary>
    public int? MaxBytes { get; init; }

    /// <summary>
    /// Amount idle time the server should wait before sending a heartbeat. For requests with expires > 30s, heartbeats should be enabled by default
    /// </summary>
    public TimeSpan? IdleHeartbeat { get; init; }
}
