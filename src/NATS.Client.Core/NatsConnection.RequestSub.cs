using System.Buffers;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal async ValueTask<INatsSub> RequestSubAsync(
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var replyTo = $"{InboxPrefix}{Guid.NewGuid():n}";
        var sub = new NatsSub(this, SubscriptionManager.InboxSubBuilder, replyTo, replyOpts);
        await SubAsync(replyTo, replyOpts, sub, cancellationToken).ConfigureAwait(false);
        await PubAsync(subject, replyTo, payload, requestOpts?.Headers, cancellationToken).ConfigureAwait(false);
        return sub;
    }

    internal async ValueTask<INatsSub<TReply>> RequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var replyTo = $"{InboxPrefix}{Guid.NewGuid():n}";

        var replySerializer = replyOpts?.Serializer ?? Options.Serializer;
        var sub = new NatsSub<TReply>(this, SubscriptionManager.InboxSubBuilder, replyTo, replyOpts, replySerializer);
        await SubAsync(replyTo, replyOpts, sub, cancellationToken).ConfigureAwait(false);

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
