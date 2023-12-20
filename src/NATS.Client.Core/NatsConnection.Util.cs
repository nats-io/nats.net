using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    // DirectWrite is not supporting CancellationTimer
    internal async ValueTask DirectWriteAsync(string protocol, int repeatCount = 1)
    {
        if (ConnectionState != NatsConnectionState.Open)
        {
            await ConnectAsync().ConfigureAwait(false);
        }

        await CommandWriter.DirectWriteAsync(protocol, repeatCount, CancellationToken.None).ConfigureAwait(false);
    }
}
