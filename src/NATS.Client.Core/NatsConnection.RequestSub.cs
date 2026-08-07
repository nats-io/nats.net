using System.Diagnostics;
using NATS.Client.Core.Internal;

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
#pragma warning disable CS0618 // SkipSubjectValidation is obsolete but still honored
        if (!Opts.SkipSubjectValidation)
#pragma warning restore CS0618
        {
            SubjectValidator.ValidateSubject(subject);
        }

        var replyTo = NewInbox();

        replySerializer ??= Opts.SerializerRegistry.GetDeserializer<TReply>();
        var sub = new NatsSub<TReply>(this, _subscriptionManager.InboxSubBuilder, replyTo, queueGroup: default, replyOpts, replySerializer)
        {
            // Set from the caller's current activity. On the request path that starts a NATS
            // request activity this is that activity, so replies arriving without trace
            // context are traced under their request. Paths that start no request activity,
            // RequestMany and the JetStream shared-inbox publish and API calls, supply their
            // caller's ambient span instead: a weaker link, but still the work that caused
            // the request. Only ids are captured, so this never keeps an activity alive.
            ReplyParentContext = Activity.Current?.Context ?? default,
        };
        await AddSubAsync(sub, cancellationToken).ConfigureAwait(false);

        requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();
        await PublishAsync(subject, data, headers, replyTo, requestSerializer, requestOpts, cancellationToken).ConfigureAwait(false);

        return sub;
    }
}
