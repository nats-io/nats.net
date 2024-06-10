#if !NET6_0_OR_GREATER
using NATS.Client.Core.Internal.NetStandardExtensions;
#endif

namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal ValueTask SubAsync(NatsSubBase sub, CancellationToken cancellationToken = default) =>
        ConnectionState != NatsConnectionState.Open
            ? ConnectAndSubAsync(sub, cancellationToken)
            : SubscriptionManager.SubscribeAsync(sub, cancellationToken);

    private async ValueTask ConnectAndSubAsync(NatsSubBase sub, CancellationToken cancellationToken = default)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await SubscriptionManager.SubscribeAsync(sub, cancellationToken).ConfigureAwait(false);
    }
}
