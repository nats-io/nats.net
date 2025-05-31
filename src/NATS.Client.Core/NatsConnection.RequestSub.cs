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
        var props = new NatsPublishProps(subject)
        {
            InboxPrefix = InboxPrefix,
        };

        props.SetReplyTo(NewInbox());
        replySerializer ??= Opts.SerializerRegistry.GetDeserializer<TReply>();
        requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();

        return await CreateRequestSubInternalAsync<TRequest, TReply>(props, data, headers, requestSerializer, replySerializer, replyOpts, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<NatsSub<TReply>> CreateRequestSubInternalAsync<TRequest, TReply>(
        NatsPublishProps props,
        TRequest? data,
        NatsHeaders? headers,
        INatsSerialize<TRequest> requestSerializer,
        INatsDeserialize<TReply> replySerializer,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(props.ReplyTo))
        {
            props.SetReplyTo(NewInbox());
        }

        NatsSubscriptionProps subProps;
        if (props.SubjectId != null)
        {
            subProps = new NatsSubscriptionProps(props.Subject, props.SubjectId);
        }
        else
        {
            subProps = new NatsSubscriptionProps(props.Subject);
        }

        var sub = new NatsSub<TReply>(this, _subscriptionManager.InboxSubBuilder, subProps, replyOpts, replySerializer);
        await AddSubAsync(sub, cancellationToken).ConfigureAwait(false);
        await PublishInternalAsync(props, requestSerializer, data, headers, null, cancellationToken).ConfigureAwait(false);

        return sub;
    }
}
