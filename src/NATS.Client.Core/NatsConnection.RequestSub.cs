namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal async ValueTask<NatsSub<TReply>> RequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var replyTo = NewInbox();

        replySerializer ??= Opts.SerializerRegistry.GetDeserializer<TReply>();
        var sub = new NatsSub<TReply>(this, SubscriptionManager.InboxSubBuilder, replyTo, queueGroup: default, replyOpts, replySerializer);
        await SubAsync(sub, cancellationToken).ConfigureAwait(false);

        requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();

        if (requestOpts?.WaitUntilSent == true)
        {
            await PubModelAsync(subject, data, requestSerializer, replyTo, headers, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PubModelPostAsync(subject, data, requestSerializer, replyTo, headers, requestOpts?.ErrorHandler, cancellationToken).ConfigureAwait(false);
        }

        return sub;
    }
}
