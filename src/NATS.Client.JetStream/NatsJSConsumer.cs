using System.Runtime.CompilerServices;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSConsumer
{
    private readonly NatsJSContext _context;
    private readonly ConsumerInfo _info;
    private readonly string _stream;
    private readonly string _consumer;

    public NatsJSConsumer(NatsJSContext context, ConsumerInfo info)
    {
        _context = context;
        _info = info;
        _stream = _info.StreamName;
        _consumer = _info.Name;
    }

    public async IAsyncEnumerable<NatsMsg> ConsumeAsync(
        ConsumerGetnextRequest request,
        NatsSubOpts requestOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        await using var sub = await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            builder: NatsSubBuilder.Default,
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

    public async IAsyncEnumerable<NatsMsg<T>> ConsumeAsync<T>(
        ConsumerGetnextRequest request,
        NatsSubOpts requestOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        await using var sub = await _context.Nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            builder: NatsSubModelBuilder<T>.For(requestOpts.Serializer ?? _context.Nats.Options.Serializer),
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
}
