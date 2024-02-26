using System.Diagnostics;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal async ValueTask<NatsSub<TReply>> RequestSubAsync<TRequest, TReply>(
        ActivitySource activitySource,
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
        var sub = new NatsSub<TReply>(activitySource, this, SubscriptionManager.InboxSubBuilder, replyTo, queueGroup: default, replyOpts, replySerializer);
        await SubAsync(sub, cancellationToken).ConfigureAwait(false);

        requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();
        await PublishAsync(activitySource, subject, data, headers, replyTo, requestSerializer, cancellationToken).ConfigureAwait(false);

        return sub;
    }
}
