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
        var props = requestOpts?.Props ?? new NatsPublishProps(subject);
        props.SetReplyTo(NewInbox());
        return await CreateRequestSubAsync(props, data, headers, requestSerializer, replySerializer, replyOpts, cancellationToken).ConfigureAwait(false);
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
        var subProps = replyOpts?.Props ?? new NatsSubscribeProps(props.Subject);
        subProps.SubscriptionId = _subscriptionManager.GetNextSid();
        var sub = new NatsSub<TReply>(this, _subscriptionManager.InboxSubBuilder, subProps, replyOpts, replySerializer);
        await AddSubAsync(sub, cancellationToken).ConfigureAwait(false);

        requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();
        await PublishAsync(props, data, headers, requestSerializer, cancellationToken).ConfigureAwait(false);

        return sub;
    }
}
