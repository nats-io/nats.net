using Microsoft.Extensions.Logging;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc/>
    public async ValueTask ReconnectAsync()
    {
        if (ConnectionState != NatsConnectionState.Open)
        {
            return;
        }

        _logger.LogInformation(NatsLogEvents.Connection, "Forcing reconnection to NATS server");
        await _socketConnection!.DisposeAsync().ConfigureAwait(false);
    }
}
