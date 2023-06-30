using System.Collections.Concurrent;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask<NatsSub> SubscribeAsync(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        return SubAsync<NatsSub>(subject, opts, NatsSubBuilder.Default, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<NatsSub<T>> SubscribeAsync<T>(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var serializer = opts?.Serializer ?? Options.Serializer;
        return SubAsync<NatsSub<T>>(subject, opts, NatsSubModelBuilder<T>.For(serializer), cancellationToken);
    }
}
