namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal async ValueTask<INatsSub<TReply>> RequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerializer2<TRequest>? requestSerializer = default,
        INatsDeserializer<TReply>? replyDeserializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var replyTo = NewInbox();

        replyDeserializer ??= Opts.SerializerRegistry.GetDeserializer<TReply>();
        var sub = new NatsSub<TReply>(this, SubscriptionManager.InboxSubBuilder, replyTo, queueGroup: default, replyOpts, replyDeserializer);
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
