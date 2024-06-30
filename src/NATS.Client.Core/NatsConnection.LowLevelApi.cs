namespace NATS.Client.Core;

public partial class NatsConnection
{
    public ValueTask SubAsync(NatsSubBase sub, CancellationToken cancellationToken = default) =>
        ConnectionState != NatsConnectionState.Open
            ? ConnectAndSubAsync(sub, cancellationToken)
            : SubscriptionManager.SubscribeAsync(sub, cancellationToken);

    private async ValueTask ConnectAndSubAsync(NatsSubBase sub, CancellationToken cancellationToken = default)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await SubscriptionManager.SubscribeAsync(sub, cancellationToken).ConfigureAwait(false);
    }
}
