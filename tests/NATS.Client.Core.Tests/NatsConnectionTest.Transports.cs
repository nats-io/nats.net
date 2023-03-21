namespace NATS.Client.Core.Tests;

public class NatsConnectionTestTcp : NatsConnectionTest
{
    public NatsConnectionTestTcp(ITestOutputHelper output)
        : base(output, TransportType.Tcp)
    {
    }
}

public class NatsConnectionTestTls : NatsConnectionTest
{
    public NatsConnectionTestTls(ITestOutputHelper output)
        : base(output, TransportType.Tls)
    {
    }
}

public class NatsConnectionTestWs : NatsConnectionTest
{
    public NatsConnectionTestWs(ITestOutputHelper output)
        : base(output, TransportType.WebSocket)
    {
    }
}
