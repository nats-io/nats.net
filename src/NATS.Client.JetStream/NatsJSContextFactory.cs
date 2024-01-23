using NATS.Client.Core;

namespace NATS.Client.JetStream;

public sealed class NatsJSContextFactory : INatsJSContextFactory
{
    public INatsJSContext CreateContext(INatsConnection connection)
    {
        var con = connection as NatsConnection ?? throw new ArgumentException("Connection must be a NatsConnection");

        return new NatsJSContext(con);
    }

    public INatsJSContext CreateContext(INatsConnection connection, NatsJSOpts opts)
    {
        var con = connection as NatsConnection ?? throw new ArgumentException("Connection must be a NatsConnection");

        return new NatsJSContext(con, opts);
    }
}
