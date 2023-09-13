using System.Buffers;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal async ValueTask<INatsSub> RequestSubAsync(
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var replyTo = $"{InboxPrefix}{Guid.NewGuid():n}";
        var sub = new NatsSub(this, SubscriptionManager.InboxSubBuilder, replyTo, queueGroup: default, replyOpts);
        await SubAsync(replyTo, queueGroup: default, replyOpts, sub, cancellationToken).ConfigureAwait(false);
        if (requestOpts?.WaitUntilSent == true)
        {
            await PubAsync(subject, replyTo, payload, headers, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PubPostAsync(subject, replyTo, payload, headers, cancellationToken).ConfigureAwait(false);
        }

        return sub;
    }

    internal async ValueTask<INatsSub<TReply>> RequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var replyTo = $"{InboxPrefix}{Guid.NewGuid():n}";

        var replySerializer = replyOpts?.Serializer ?? Opts.Serializer;
        var sub = new NatsSub<TReply>(this, SubscriptionManager.InboxSubBuilder, replyTo, queueGroup: default, replyOpts, replySerializer);
        await SubAsync(replyTo, queueGroup: default, replyOpts, sub, cancellationToken).ConfigureAwait(false);

        var serializer = requestOpts?.Serializer ?? Opts.Serializer;

        if (requestOpts?.WaitUntilSent == true)
        {
            await PubModelAsync(subject, data, serializer, replyTo, headers, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PubModelPostAsync(subject, data, serializer, replyTo, headers, cancellationToken).ConfigureAwait(false);
        }

        return sub;
    }
}
