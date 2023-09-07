namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public async ValueTask<INatsSub> SubscribeAsync(string subject, string? queueGroup = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var sub = new NatsSub(this, SubscriptionManager, subject, queueGroup, opts);
        await SubAsync(subject, queueGroup, opts, sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }

    /// <inheritdoc />
    public async ValueTask<INatsSub<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var serializer = opts?.Serializer ?? Opts.Serializer;
        var sub = new NatsSub<T>(this, SubscriptionManager.GetManagerFor(subject), subject, queueGroup, opts, serializer);
        await SubAsync(subject, queueGroup, opts, sub, cancellationToken).ConfigureAwait(false);
        return sub;
    }
}
