using Microsoft.Extensions.Logging;

namespace NATS.Client.Core;

public interface ISocketConnectionFactory
{
    public Task<ISocketConnection> OnConnectionAsync(NatsUri uri, NatsConnection natsConnection, ILogger<NatsConnection> logger);

    public Task<ISocketConnection> OnReconnectionAsync(NatsUri uri, NatsConnection natsConnection, ILogger<NatsConnection> logger, int reconnectCount);
}
