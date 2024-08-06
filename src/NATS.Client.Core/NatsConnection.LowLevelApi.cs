namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal ValueTask SubAsync(NatsSubBase sub, CancellationToken cancellationToken = default) =>
        ConnectionState != NatsConnectionState.Open
            ? ConnectAndSubAsync(sub, cancellationToken)
            : _subscriptionManager.SubscribeAsync(sub, cancellationToken);

    private async ValueTask ConnectAndSubAsync(NatsSubBase sub, CancellationToken cancellationToken = default)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await _subscriptionManager.SubscribeAsync(sub, cancellationToken).ConfigureAwait(false);
    }
}
