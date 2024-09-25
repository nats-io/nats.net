using NATS.Client.Core;
using NATS.Client.Services;

// ReSharper disable once CheckNamespace
namespace NATS.Net;

public static class NatsClientExtensions
{
    /// <summary>
    /// Creates a NATS Services context for the given NATS client.
    /// </summary>
    /// <param name="client">The NATS client for which to create the services context.</param>
    /// <returns>An instance of <see cref="INatsSvcContext"/> used for interacting with the NATS Services.</returns>
    public static INatsSvcContext CreateServicesContext(this INatsClient client)
        => CreateServicesContext(client.Connection);

    /// <summary>
    /// Creates a NATS Services context for the given NATS connection.
    /// </summary>
    /// <param name="connection">The NATS connection for which to create the services context.</param>
    /// <returns>An instance of <see cref="INatsSvcContext"/> used for interacting with the NATS Services.</returns>
    public static INatsSvcContext CreateServicesContext(this INatsConnection connection)
        => new NatsSvcContext(connection);
}
