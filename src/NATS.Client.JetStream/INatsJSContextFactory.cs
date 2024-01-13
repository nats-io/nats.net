using NATS.Client.Core;

namespace NATS.Client.JetStream;

internal interface INatsJSContextFactory
{
    INatsJSContext CreateContext(INatsConnection connection);

    INatsJSContext CreateContext(INatsConnection connection, NatsJSOpts opts);
}
