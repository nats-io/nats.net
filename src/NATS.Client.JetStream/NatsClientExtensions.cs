using NATS.Client.Core;

namespace NATS.Client.JetStream;

public static class NatsClientExtensions
{
    public static INatsJSContext CreateJetStreamContext(this INatsClient client)
        => CreateJetStreamContext(client.Connection);

    public static INatsJSContext CreateJetStreamContext(this INatsConnection connection)
        => new NatsJSContext(connection);
}
