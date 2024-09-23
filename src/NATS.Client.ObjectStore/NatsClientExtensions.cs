using NATS.Client.Core;
using NATS.Client.JetStream;

namespace NATS.Client.ObjectStore;

public static class NatsClientExtensions
{
    /// <summary>
    /// Creates a NATS Object Store context for the given NATS client.
    /// </summary>
    /// <param name="client">The NATS client instance.</param>
    /// <returns>An instance of <see cref="INatsObjContext"/> used for interacting with the NATS Object Store.</returns>
    public static INatsObjContext CreateObjectStoreContext(this INatsClient client)
        => CreateObjectStoreContext(client.Connection);

    /// <summary>
    /// Creates a NATS Object Store context for the given NATS connection.
    /// </summary>
    /// <param name="connection">The NATS connection instance.</param>
    /// <returns>An instance of <see cref="INatsObjContext"/> used for interacting with the NATS Object Store.</returns>
    public static INatsObjContext CreateObjectStoreContext(this INatsConnection connection)
        => CreateObjectStoreContext(new NatsJSContext(connection));

    /// <summary>
    /// Creates a NATS Object Store context for the given NATS JetStream context.
    /// </summary>
    /// <param name="context">The NATS JetStream context instance.</param>
    /// <returns>An instance of <see cref="INatsObjContext"/> used for interacting with the NATS Object Store.</returns>
    public static INatsObjContext CreateObjectStoreContext(this INatsJSContext context)
        => new NatsObjContext(context);
}
