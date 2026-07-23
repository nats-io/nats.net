namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask AddSubAsync(NatsSubBase sub, CancellationToken cancellationToken = default) =>
        ConnectionState != NatsConnectionState.Open || sub.Opts?.Events?.OnSubscribed is not null
            ? AddSubInternalAsync(sub, cancellationToken)
            : _subscriptionManager.SubscribeAsync(sub, cancellationToken);

    private async ValueTask AddSubInternalAsync(NatsSubBase sub, CancellationToken cancellationToken = default)
    {
        if (ConnectionState != NatsConnectionState.Open)
            await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

        await _subscriptionManager.SubscribeAsync(sub, cancellationToken).ConfigureAwait(false);

        if (sub.Opts?.Events?.OnSubscribed is { } onSubscribed)
        {
            try
            {
                await onSubscribed(sub).ConfigureAwait(false);
            }
            catch
            {
                await sub.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
