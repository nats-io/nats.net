using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSConsumer
{
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
    }

    public ConsumerInfo Info { get; }

    public async ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _deleted = await _context.DeleteConsumerAsync(_stream, _consumer, cancellationToken);
    }

    public async IAsyncEnumerable<NatsJSMsg<T?>> ConsumeAllAsync<T>(
        NatsJSConsumeOpts opts,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var cc = await ConsumeAsync<T>(opts, cancellationToken);
        await foreach (var jsMsg in cc.Msgs.ReadAllAsync(cancellationToken))
        {
            yield return jsMsg;
        }
    }

    public async ValueTask<INatsJSSubConsume<T>> ConsumeAsync<T>(NatsJSConsumeOpts opts, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        var inbox = _context.NewInbox();

        var max = NatsJSOpsDefaults.SetMax(_context.Opts, opts.MaxMsgs, opts.MaxBytes, opts.ThresholdMsgs, opts.ThresholdBytes);
        var timeouts = NatsJSOpsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);

        var requestOpts = new NatsSubOpts
        {
            Serializer = opts.Serializer,
            ChannelOptions = new NatsSubChannelOpts
            {
                // Keep capacity at 1 to make sure message acknowledgements are sent
                // right after the message is processed and messages aren't queued up
                // which might cause timeouts for acknowledgments.
                Capacity = 1,
                FullMode = BoundedChannelFullMode.Wait,
            },
        };

        var sub = new NatsJSSubConsume<T>(
            stream: _stream,
            consumer: _consumer,
            context: _context,
            subject: inbox,
            opts: requestOpts,
            maxMsgs: max.MaxMsgs,
            maxBytes: max.MaxBytes,
            thresholdMsgs: max.ThresholdMsgs,
            thresholdBytes: max.ThresholdBytes,
            expires: timeouts.Expires,
            idle: timeouts.IdleHeartbeat,
            errorHandler: opts.ErrorHandler);

        await _context.Connection.SubAsync(
            subject: inbox,
            opts: requestOpts,
            sub: sub,
            cancellationToken);

        await sub.CallMsgNextAsync(
            new ConsumerGetnextRequest
            {
                Batch = max.MaxMsgs,
                MaxBytes = max.MaxBytes,
                IdleHeartbeat = timeouts.IdleHeartbeat.ToNanos(),
                Expires = timeouts.Expires.ToNanos(),
            },
            cancellationToken);

        sub.ResetPending();
        sub.ResetHeartbeatTimer();

        return sub;
    }

    public async ValueTask<NatsJSMsg<T?>?> NextAsync<T>(NatsJSNextOpts opts, CancellationToken cancellationToken = default)
    {
        await using var f = await FetchAsync<T>(
            new NatsJSFetchOpts
            {
                MaxMsgs = 1,
                IdleHeartbeat = opts.IdleHeartbeat,
                Expires = opts.Expires,
                Serializer = opts.Serializer,
                ErrorHandler = opts.ErrorHandler,
            },
            cancellationToken: cancellationToken);

        await foreach (var natsJSMsg in f.Msgs.ReadAllAsync(cancellationToken))
        {
            return natsJSMsg;
        }

        return default;
    }

    public async IAsyncEnumerable<NatsJSMsg<T?>> FetchAllAsync<T>(
        NatsJSFetchOpts opts,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var fc = await FetchAsync<T>(opts, cancellationToken);
        await foreach (var jsMsg in fc.Msgs.ReadAllAsync(cancellationToken))
        {
            yield return jsMsg;
        }
    }

    public async ValueTask<INatsJSSubFetch<T>> FetchAsync<T>(
        NatsJSFetchOpts opts,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        var inbox = _context.NewInbox();

        var max = NatsJSOpsDefaults.SetMax(_context.Opts, opts.MaxMsgs, opts.MaxBytes);
        var timeouts = NatsJSOpsDefaults.SetTimeouts(opts.Expires, opts.IdleHeartbeat);

        var requestOpts = new NatsSubOpts
        {
            Serializer = opts.Serializer,
            ChannelOptions = new NatsSubChannelOpts
            {
                // Keep capacity at 1 to make sure message acknowledgements are sent
                // right after the message is processed and messages aren't queued up
                // which might cause timeouts for acknowledgments.
                Capacity = 1,
                FullMode = BoundedChannelFullMode.Wait,
            },
        };

        var sub = new NatsJSSubFetch<T>(
            stream: _stream,
            consumer: _consumer,
            context: _context,
            subject: inbox,
            opts: requestOpts,
            maxMsgs: max.MaxMsgs,
            maxBytes: max.MaxBytes,
            expires: timeouts.Expires,
            idle: timeouts.IdleHeartbeat,
            errorHandler: opts.ErrorHandler);

        await _context.Connection.SubAsync(
            subject: inbox,
            opts: requestOpts,
            sub: sub,
            cancellationToken);

        await sub.CallMsgNextAsync(
            new ConsumerGetnextRequest
            {
                Batch = max.MaxMsgs,
                MaxBytes = max.MaxBytes,
                IdleHeartbeat = timeouts.IdleHeartbeat.ToNanos(),
                Expires = timeouts.Expires.ToNanos(),
            },
            cancellationToken);

        sub.ResetHeartbeatTimer();

        return sub;
    }

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }
}
