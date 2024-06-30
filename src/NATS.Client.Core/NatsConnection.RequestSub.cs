namespace NATS.Client.Core;

public partial class NatsConnection
{
    public async ValueTask<NatsSub<TReply>> RequestSubAsync<TRequest, TReply>(
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
        await PublishAsync(subject, data, headers, replyTo, requestSerializer, requestOpts, cancellationToken).ConfigureAwait(false);

        return sub;
    }
}
