using System.Buffers;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal async ValueTask<NatsSub> RequestSubAsync(
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var replyTo = $"{InboxPrefix}{Guid.NewGuid():n}";
        var sub = await SubAsync(replyTo, replyOpts, NatsSubBuilder.Default, cancellationToken).ConfigureAwait(false);
        await PubAsync(subject, replyTo, payload, requestOpts?.Headers, cancellationToken).ConfigureAwait(false);
        return sub;
    }

    internal async ValueTask<NatsSub<TReply>> RequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var replyTo = $"{InboxPrefix}{Guid.NewGuid():n}";

        var builder = NatsSubModelBuilder<TReply>.For(replyOpts?.Serializer ?? Options.Serializer);
        var sub = await SubAsync(replyTo, replyOpts, builder, cancellationToken).ConfigureAwait(false);

        await PubModelAsync(
            subject,
            data,
            requestOpts?.Serializer ?? Options.Serializer,
            replyTo,
            requestOpts?.Headers,
            cancellationToken).ConfigureAwait(false);

        return sub;
    }
}
