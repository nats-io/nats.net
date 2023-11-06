namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public async ValueTask<INatsSub<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserializer<T>? deserializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        deserializer ??= Opts.SerializerRegistry.GetDeserializer<T>();
        var sub = new NatsSub<T>(this, SubscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, deserializer);
        await SubAsync(sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }
}
