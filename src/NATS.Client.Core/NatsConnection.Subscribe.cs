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
        serializer ??= Opts.SerializerRegistry.GetDeserializer<T>();

        await using var sub = new NatsSub<T>(Telemetry.NatsActivities, this, SubscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer, cancellationToken);
        using var anchor = RegisterSubAnchor(sub);

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

    /// <inheritdoc />
    public async ValueTask<INatsSub<T>> SubscribeCoreAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.SerializerRegistry.GetDeserializer<T>();
        var sub = new NatsSub<T>(Telemetry.NatsActivities, this, SubscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer, cancellationToken);
        await SubAsync(sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }

    /// <summary>
    /// Make sure subscription is not collected until the end of the scope.
    /// </summary>
    /// <param name="sub">Subscription object</param>
    /// <returns>Disposable</returns>
    /// <remarks>
    /// We must keep subscription alive until the end of its scope especially in async iterators.
    /// Otherwise subscription is collected because subscription manager only holds a weak reference to it.
    /// </remarks>
    internal IDisposable RegisterSubAnchor(NatsSubBase sub) => new SubAnchor(this, sub);

    internal class SubAnchor : IDisposable
    {
        private readonly NatsConnection _nats;
        private readonly long _anchor;

        public SubAnchor(NatsConnection nats, NatsSubBase sub)
        {
            _nats = nats;
            _anchor = Interlocked.Increment(ref _nats._subAnchorId);
            _nats._subAnchor[_anchor] = sub;
        }

        public void Dispose() => _nats._subAnchor.TryRemove(_anchor, out _);
    }
}
