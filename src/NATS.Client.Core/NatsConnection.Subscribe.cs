using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public async IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
    public async ValueTask<INatsSub<T>> SubscribeCoreAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.SerializerRegistry.GetDeserializer<T>();
        var sub = new NatsSub<T>(this, _subscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer, cancellationToken);
        await AddSubAsync(sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }
}
