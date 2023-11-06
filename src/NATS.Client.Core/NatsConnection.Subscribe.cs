namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public async ValueTask<INatsSub<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsSerializer<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
        var sub = new NatsSub<T>(this, SubscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer);
        await SubAsync(sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }
}
