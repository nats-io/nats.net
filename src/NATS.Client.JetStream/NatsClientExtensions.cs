using NATS.Client.Core;
using NATS.Client.JetStream;

// ReSharper disable once CheckNamespace
namespace NATS.Net;

public static class NatsClientExtensions
{
    /// <summary>
    /// Creates a JetStream context using the provided NATS client.
    /// </summary>
    /// <param name="client">The NATS client used to create the JetStream context.</param>
    /// <returns>Returns an instance of <see cref="INatsJSContext"/> for interacting with JetStream.</returns>
    public static INatsJSContext CreateJetStreamContext(this INatsClient client)
        => CreateJetStreamContext(client.Connection);

    /// <summary>
    /// Creates a JetStream context using the provided NATS connection.
    /// </summary>
    /// <param name="connection">The NATS connection used to create the JetStream context.</param>
    /// <returns>Returns an instance of <see cref="INatsJSContext"/> for interacting with JetStream.</returns>
    public static INatsJSContext CreateJetStreamContext(this INatsConnection connection)
        => new NatsJSContext(connection);

    /// <summary>
    /// Creates a JetStream context using the provided NATS client.
    /// </summary>
    /// <param name="client">The NATS client used to create the JetStream context.</param>
    /// <param name="opts">Context options.</param>
    /// <returns>Returns an instance of <see cref="INatsJSContext"/> for interacting with JetStream.</returns>
    public static INatsJSContext CreateJetStreamContext(this INatsClient client, NatsJSOpts opts)
        => CreateJetStreamContext(client.Connection, opts);

    /// <summary>
    /// Creates a JetStream context using the provided NATS connection.
    /// </summary>
    /// <param name="connection">The NATS connection used to create the JetStream context.</param>
    /// <param name="opts">Context options.</param>
    /// <returns>Returns an instance of <see cref="INatsJSContext"/> for interacting with JetStream.</returns>
    public static INatsJSContext CreateJetStreamContext(this INatsConnection connection, NatsJSOpts opts)
        => new NatsJSContext(connection, opts);
}
