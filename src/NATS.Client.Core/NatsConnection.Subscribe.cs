using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        // Validate synchronously before returning the async enumerable
        // so that invalid subjects throw immediately when SubscribeAsync is called
        if (!Opts.SkipSubjectValidation)
        {
            SubjectValidator.ValidateSubject(subject);
            SubjectValidator.ValidateQueueGroup(queueGroup);
        }

        return SubscribeInternalAsync<T>(subject, queueGroup, serializer, opts, cancellationToken);
    }

    private async IAsyncEnumerable<NatsMsg<T>> SubscribeInternalAsync<T>(string subject, string? queueGroup, INatsDeserialize<T>? serializer, NatsSubOpts? opts, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        serializer ??= Opts.SerializerRegistry.GetDeserializer<T>();

        await using var sub = new NatsSub<T>(this, _subscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer, cancellationToken);
        await AddSubAsync(sub, cancellationToken: cancellationToken).ConfigureAwait(false);

        // We don't cancel the channel reader here because we want to keep reading until the subscription
        // channel writer completes so that messages left in the channel can be consumed before exit the loop.
        await foreach (var msg in sub.Msgs.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            yield return msg;
        }
    }

    /// <inheritdoc />
    public ValueTask<INatsSub<T>> SubscribeCoreAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        // Validate synchronously so invalid subjects throw immediately
        if (!Opts.SkipSubjectValidation)
        {
            SubjectValidator.ValidateSubject(subject);
            SubjectValidator.ValidateQueueGroup(queueGroup);
        }

        return SubscribeCoreInternalAsync<T>(subject, queueGroup, serializer, opts, cancellationToken);
    }

    private async ValueTask<INatsSub<T>> SubscribeCoreInternalAsync<T>(string subject, string? queueGroup, INatsDeserialize<T>? serializer, NatsSubOpts? opts, CancellationToken cancellationToken)
    {
        serializer ??= Opts.SerializerRegistry.GetDeserializer<T>();
        var sub = new NatsSub<T>(this, _subscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer, cancellationToken);
        await AddSubAsync(sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }
}
