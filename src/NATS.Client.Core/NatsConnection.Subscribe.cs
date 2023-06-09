namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask<NatsSub> SubscribeAsync(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var natsSub = new NatsSub(subject)
        {
            Connection = this,
            QueueGroup = opts?.QueueGroup ?? string.Empty,
        };

        NatsKey? queueGroup = null;
        if (!string.IsNullOrWhiteSpace(opts?.QueueGroup))
        {
            queueGroup = new NatsKey(opts.Value.QueueGroup);
        }

        if (ConnectionState == NatsConnectionState.Open)
        {
            natsSub.InternalSubscription = _subscriptionManager.AddAsync<ReadOnlyMemory<byte>>(subject, queueGroup, natsSub, cancellationToken);
            return new ValueTask<NatsSub>(natsSub);
        }
        else
        {
            return WithConnectAsync(subject, queueGroup, natsSub, cancellationToken, static (self, key, qg, handler, token) =>
            {
                handler.InternalSubscription = self._subscriptionManager.AddAsync<ReadOnlyMemory<byte>>(key, qg, handler, token);
                return new ValueTask<NatsSub>(handler);
            });
        }
    }

    /// <inheritdoc />
    public ValueTask<NatsSub<T>> SubscribeAsync<T>(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var natsSub = new NatsSub<T>(subject)
        {
            Connection = this,
            QueueGroup = opts?.QueueGroup ?? string.Empty,
        };

        NatsKey? queueGroup = null;
        if (!string.IsNullOrWhiteSpace(opts?.QueueGroup))
        {
            queueGroup = new NatsKey(opts.Value.QueueGroup);
        }

        if (ConnectionState == NatsConnectionState.Open)
        {
            natsSub.InternalSubscription = _subscriptionManager.AddAsync<ReadOnlyMemory<byte>>(subject, queueGroup, natsSub, cancellationToken);
            return new ValueTask<NatsSub<T>>(natsSub);
        }
        else
        {
            return WithConnectAsync(subject, queueGroup, natsSub, cancellationToken, static (self, key, qg, handler, token) =>
            {
                handler.InternalSubscription = self._subscriptionManager.AddAsync<ReadOnlyMemory<byte>>(key, qg, handler, token);
                return new ValueTask<NatsSub<T>>(handler);
            });
        }
    }
}
