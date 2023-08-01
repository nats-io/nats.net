using System.Runtime.CompilerServices;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSConsumer
{
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

        var pending = new Pending(opts);

        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        var requestOpts = default(NatsSubOpts);

        await using var sub = await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            builder: NatsJSSubModelBuilder<T>.For(requestOpts.Serializer ?? _context.Nats.Options.Serializer),
            cancellationToken);

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

        await MsgNextAsync(_context, _stream, _consumer, pending.GetRequest(), inbox, cancellationToken);

        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
        {
            if (msg.IsControlMsg)
            {
                // TODO: Heartbeats etc.
                Console.WriteLine("XXX");
            }
            else
            {
                var jsMsg = msg.JSMsg!.Value;

                pending.MsgReceived(jsMsg.Msg.Size);

                yield return jsMsg;

                if (pending.CanFetch())
                {
                    await MsgNextAsync(_context, _stream, _consumer, pending.GetRequest(), inbox, cancellationToken);
                }
            }
        }

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }
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
                // TODO: Heartbeats etc.
            }
            else
            {
                yield return msg.JSMsg!.Value;

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

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }

    internal class Pending
    {
        private readonly NatsJSConsumeOpts _opts;

        public Pending(NatsJSConsumeOpts opts) => _opts = opts;

        public long Msgs { get; set; }

        public long Bytes { get; set; }

        public long Requests { get; set; }

        public ConsumerGetnextRequest GetRequest()
        {
            Requests++;
            var request = new ConsumerGetnextRequest
            {
                Batch = _opts.MaxBytes > 0 ? 1_000_000 : _opts.MaxMsgs - Msgs,
                MaxBytes = _opts.MaxBytes > 0 ? _opts.MaxBytes - Bytes : 0,
                IdleHeartbeat = (long)(_opts.IdleHeartbeat.TotalMilliseconds * 1_000),
                Expires = (long)(_opts.Expires.TotalMilliseconds * 1_000),
            };

            Msgs += request.Batch;
            Bytes += request.MaxBytes;

            return request;
        }

        public void MsgReceived(int size)
        {
            Msgs--;
            Bytes -= size;
        }

        public bool CanFetch() =>
            _opts.ThresholdMsgs >= Msgs || (_opts.ThresholdBytes > 0 && _opts.ThresholdBytes >= Bytes);
    }
}

public record NatsJSConsumeOpts
{
    private static readonly TimeSpan DefaultExpires = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinExpires = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatCap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatMin = TimeSpan.FromSeconds(.5);

    public NatsJSConsumeOpts(
        int? maxMsgs = default,
        TimeSpan? expires = default,
        int? maxBytes = default,
        TimeSpan? idleHeartbeat = default,
        int? thresholdMsgs = default,
        int? thresholdBytes = default)
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

        Expires = expires ?? DefaultExpires;
        if (Expires < MinExpires)
            Expires = MinExpires;

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
    }

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
