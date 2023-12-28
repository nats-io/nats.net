namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal ValueTask SubAsync(NatsSubBase sub, CancellationToken cancellationToken = default) =>
        ConnectionState != NatsConnectionState.Open
            ? ConnectAndSubAsync(sub, cancellationToken)
            : SubscriptionManager.SubscribeAsync(sub, cancellationToken);

    private async ValueTask ConnectAndSubAsync(NatsSubBase sub, CancellationToken cancellationToken = default)
    {
        var connect = ConnectAsync();
        if (connect.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD103
            connect.GetAwaiter().GetResult();
#pragma warning restore VSTHRD103
        }
        else
        {
            await connect.AsTask().WaitAsync(Opts.CommandTimeout, cancellationToken).ConfigureAwait(false);
        }

        await SubscriptionManager.SubscribeAsync(sub, cancellationToken).ConfigureAwait(false);
    }
}
