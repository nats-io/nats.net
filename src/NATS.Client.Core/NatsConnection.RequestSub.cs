namespace NATS.Client.Core;

public partial class NatsConnection
{
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
            await PubModelPostAsync(subject, data, serializer, replyTo, headers, requestOpts?.ErrorHandler, cancellationToken).ConfigureAwait(false);
        }

        return sub;
    }
}
