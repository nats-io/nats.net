namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask<NatsSub> SubscribeAsync(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var queueGroup = opts?.QueueGroup;
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddAsync(subject, queueGroup, cancellationToken);
        }
        else
        {
            return WithConnectAsync(subject, queueGroup, cancellationToken, static (self, key, qg, token) =>
            {
                return self._subscriptionManager.AddAsync(key, qg, token);
            });
        }
    }

    /// <inheritdoc />
    public ValueTask<NatsSub<T>> SubscribeAsync<T>(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var queueGroup = opts?.QueueGroup;
        var serializer = opts?.Serializer ?? Options.Serializer;

        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddAsync<T>(subject, queueGroup, serializer, cancellationToken);
        }
        else
        {
            return WithConnectAsync(subject, queueGroup, serializer, cancellationToken, static (self, s, qg, ser, token) =>
            {
                return self._subscriptionManager.AddAsync<T>(s, qg, ser, token);
            });
        }
    }
}
