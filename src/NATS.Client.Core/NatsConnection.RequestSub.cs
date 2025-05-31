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
        requestOpts ??= new NatsPubOpts();
        requestOpts.Subject ??= subject;
        requestOpts.ReplyTo ??= NewInbox();
        requestOpts.InboxPrefix ??= InboxPrefix;

        replySerializer ??= Opts.SerializerRegistry.GetDeserializer<TReply>();
        requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();

        return await CreateRequestSubInternalAsync<TRequest, TReply>(requestOpts, data, headers, requestSerializer, replySerializer, replyOpts, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<NatsSub<TReply>> CreateRequestSubInternalAsync<TRequest, TReply>(
        NatsPubOpts requestOpts,
        TRequest? data,
        NatsHeaders? headers,
        INatsSerialize<TRequest> requestSerializer,
        INatsDeserialize<TReply> replySerializer,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var sub = new NatsSub<TReply>(this, _subscriptionManager.InboxSubBuilder, requestOpts.ReplyTo, queueGroup: default, replyOpts, replySerializer);
        await AddSubAsync(sub, cancellationToken).ConfigureAwait(false);
        await PublishInternalAsync(requestOpts, requestSerializer, data, headers, null, cancellationToken).ConfigureAwait(false);

        return sub;
    }
}
