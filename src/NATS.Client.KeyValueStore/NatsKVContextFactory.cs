using NATS.Client.JetStream;

namespace NATS.Client.KeyValueStore;

public sealed class NatsKVContextFactory : INatsKVContextFactory
{
    public INatsKVContext CreateContext(INatsJSContext jsContext)
    {
        var context = jsContext as NatsJSContext ?? throw new ArgumentException("Connection must be a NatsConnection");

        return new NatsKVContext(context);
    }
}
