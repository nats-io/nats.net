namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public async ValueTask<NatsSub<TReply>> CreateRequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var props = requestOpts?.Props ?? new NatsPublishProps(subject, InboxPrefix);
        props.SetReplyTo(NewInbox());
        replySerializer ??= Opts.SerializerRegistry.GetDeserializer<TReply>();
        var subProps = new NatsSubscriptionProps(props.Subject);
        var sub = new NatsSub<TReply>(this, _subscriptionManager.InboxSubBuilder, subProps, replyOpts, replySerializer);
        await AddSubAsync(sub, cancellationToken).ConfigureAwait(false);

        requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();
        await PublishAsync(props, data, headers, requestSerializer, cancellationToken).ConfigureAwait(false);

        return sub;
    }

    /// <inheritdoc />
    internal async ValueTask<NatsSub<TReply>> CreateRequestSubAsync<TRequest, TReply>(
        NatsPublishProps props,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        replySerializer ??= Opts.SerializerRegistry.GetDeserializer<TReply>();
        var subProps = new NatsSubscriptionProps(props.Subject);
        var sub = new NatsSub<TReply>(this, _subscriptionManager.InboxSubBuilder, subProps, replyOpts, replySerializer);
        await AddSubAsync(sub, cancellationToken).ConfigureAwait(false);

        requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();
        await PublishAsync(props, data, headers, requestSerializer, cancellationToken).ConfigureAwait(false);

        return sub;
    }
}
