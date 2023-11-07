using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    // Keep subscription alive until the channel reader completes.
    // Otherwise subscription is collected because subscription manager
    // only holds a weak reference to it.
    private readonly ConcurrentDictionary<long, NatsSubBase> _subAnchor = new();
    private long _subAnchorId;

    /// <inheritdoc />
    public async IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var anchor = Interlocked.Increment(ref _subAnchorId);
        try
        {
            serializer ??= Opts.SerializerRegistry.GetDeserializer<T>();

            await using var sub = new NatsSub<T>(this, SubscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer, cancellationToken);

            _subAnchor[anchor] = sub;

            await SubAsync(sub, cancellationToken: cancellationToken).ConfigureAwait(false);

            // We don't cancel the channel reader here because we want to keep reading until the subscription
            // channel writer completes so that messages left in the channel can be consumed before exit the loop.
            while (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
            {
                while (sub.Msgs.TryRead(out var msg))
                {
                    yield return msg;
                }
            }
        }
        finally
        {
            _subAnchor.TryRemove(anchor, out _);
        }
    }

    internal async ValueTask<NatsSub<T>> SubscribeInternalAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.SerializerRegistry.GetDeserializer<T>();
        var sub = new NatsSub<T>(this, SubscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer, cancellationToken);
        await SubAsync(sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }
}
