using NATS.Client.JetStream;

namespace NATS.Client.ObjectStore;

public interface INatsObjContextFactory
{
    INatsObjContext CreateContext(INatsJSContext jsContext);
}
