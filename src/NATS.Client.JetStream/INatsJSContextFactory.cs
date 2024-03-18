using NATS.Client.Core;

namespace NATS.Client.JetStream;

public interface INatsJSContextFactory
{
    INatsJSContext CreateContext(INatsConnection connection);

    INatsJSContext CreateContext(INatsConnection connection, NatsJSOpts opts);
}
