using Microsoft.Extensions.Logging;
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

    public async ValueTask<INatsJSSubConsume<T>> ConsumeAsync<T>(
        NatsJSConsumeOpts opts,
        NatsSubOpts requestOpts = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        var inbox = $"{_context.Opts.InboxPrefix}.{Guid.NewGuid():n}";

        var state = new NatsJSSubState(
            opts: _context.Opts,
            optsMaxBytes: opts.MaxBytes,
            optsMaxMsgs: opts.MaxMsgs,
            optsThresholdMsgs: opts.ThresholdMsgs,
            optsThresholdBytes: opts.ThresholdBytes,
            optsExpires: opts.Expires,
            optsIdleHeartbeat: opts.IdleHeartbeat);

        var sub = new NatsJSSubBaseConsume<T>(
            stream: _stream,
            consumer: _consumer,
            context: _context,
            manager: _context.Nats.SubscriptionManager,
            subject: inbox,
            opts: requestOpts,
            state: state,
            serializer: requestOpts.Serializer ?? _context.Nats.Options.Serializer,
            errorHandler: opts.ErrorHandler,
            cancellationToken: cancellationToken);

        await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            sub: sub,
            cancellationToken);

        return sub;
    }

    public async ValueTask<NatsJSMsg<T?>> NextAsync<T>(CancellationToken cancellationToken = default)
    {
        await foreach (var natsJSMsg in FetchAsync<T>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: cancellationToken))
        {
            return natsJSMsg;
        }

        throw new NatsJSException("No data");
    }

    public IAsyncEnumerable<NatsJSMsg<T?>> FetchAsync<T>(
        NatsJSFetchOpts opts,
        NatsSubOpts? requestOpts = default,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }
}
