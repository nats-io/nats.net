using NATS.Client.JetStream;

namespace NATS.Client.KeyValueStore;

public interface INatsKVContextFactory
{
    INatsKVContext CreateContext(INatsJSContext jsContext);
}
