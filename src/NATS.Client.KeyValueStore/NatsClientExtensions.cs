using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

// ReSharper disable once CheckNamespace
namespace NATS.Net;

public static class NatsClientExtensions
{
    /// <summary>
    /// Creates a NATS Key-Value Store context using the specified NATS client.
    /// </summary>
    /// <param name="client">The NATS client instance.</param>
    /// <returns>An instance of <see cref="INatsKVContext"/> which can be used to interact with the Key-Value Store.</returns>
    public static INatsKVContext CreateKeyValueStoreContext(this INatsClient client)
        => CreateKeyValueStoreContext(client.CreateJetStreamContext());

    /// <summary>
    /// Creates a NATS Key-Value Store context using the specified NATS connection.
    /// </summary>
    /// <param name="connection">The NATS connection instance.</param>
    /// <returns>An instance of <see cref="INatsKVContext"/> which can be used to interact with the Key-Value Store.</returns>
    public static INatsKVContext CreateKeyValueStoreContext(this INatsConnection connection)
        => CreateKeyValueStoreContext(connection.CreateJetStreamContext());

    /// <summary>
    /// Creates a NATS Key-Value Store context using the specified NATS JetStream context.
    /// </summary>
    /// <param name="context">The NATS JetStream context instance.</param>
    /// <returns>An instance of <see cref="INatsKVContext"/> which can be used to interact with the Key-Value Store.</returns>
    public static INatsKVContext CreateKeyValueStoreContext(this INatsJSContext context)
        => new NatsKVContext(context);
}
