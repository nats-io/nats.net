using NATS.Client.JetStream;

namespace NATS.Client.ObjectStore;

public class NatsObjContextFactory : INatsObjContextFactory
{
    public INatsObjContext CreateContext(INatsJSContext jsContext)
    {
        var context = jsContext as NatsJSContext ?? throw new ArgumentException("Connection must be a NatsConnection");

        return new NatsObjContext(context);
    }
}
