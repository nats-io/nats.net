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

    public async IAsyncEnumerable<NatsJSMsg<T?>> ConsumeAsync<T>(int maxMsgs, ConsumerOpts opts, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        var prefetch = opts.Prefetch;
        var lowWatermark = opts.LowWatermark;
        var shouldPrefetch = true;

        if (maxMsgs <= prefetch)
        {
            prefetch = maxMsgs;
            lowWatermark = maxMsgs;
            shouldPrefetch = false;
        }

        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        var requestOpts = default(NatsSubOpts);
        var request = new ConsumerGetnextRequest { Batch = prefetch };

        ConsumerGetnextRequest? fetch = default;
        if (shouldPrefetch)
        {
            fetch = new ConsumerGetnextRequest { Batch = prefetch - lowWatermark };
        }

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

        await MsgNextAsync(_context, _stream, _consumer, request, inbox, cancellationToken);

        var count = 0;
        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
        {
            if (msg.IsControlMsg)
            {
                // TODO: Heartbeats etc.
            }
            else
            {
                yield return msg.JSMsg!.Value;

                if (++count == maxMsgs)
                {
                    break;
                }

                if (shouldPrefetch && count % lowWatermark == 0)
                {
                    await MsgNextAsync(_context, _stream, _consumer, fetch!, inbox, cancellationToken);
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

        await using var sub = new NatsJSSub(_context.Nats, _context.Nats.SubscriptionManager, inbox, requestOpts);
        await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            sub: sub,
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
    }

    internal async IAsyncEnumerable<NatsJSControlMsg<T?>> ConsumeRawAsync<T>(
        ConsumerGetnextRequest request,
        NatsSubOpts requestOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        await using var sub = new NatsJSSub<T>(_context.Nats, _context.Nats.SubscriptionManager, inbox, requestOpts, requestOpts.Serializer ?? _context.Nats.Options.Serializer);
        await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            sub,
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
    }

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Consumer '{_stream}:{_consumer}' is deleted");
    }
}

public record ConsumerOpts
{
    public int Prefetch { get; set; } = 1_000;

    public int LowWatermark { get; set; } = 500;
}
