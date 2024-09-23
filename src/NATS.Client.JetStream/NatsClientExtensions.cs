using NATS.Client.Core;

namespace NATS.Client.JetStream;

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
}
