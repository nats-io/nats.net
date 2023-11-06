using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public async IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var sub = await SubscribeInternalAsync(subject, queueGroup, serializer, opts, cancellationToken).ConfigureAwait(false);
        while (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (sub.Msgs.TryRead(out var msg))
            {
                yield return msg;
            }
        }
    }

    public async ValueTask<NatsSub<T>> SubscribeInternalAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.SerializerRegistry.GetDeserializer<T>();
        var sub = new NatsSub<T>(this, SubscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer);
        await SubAsync(sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }
}
