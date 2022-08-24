namespace AlterNats.Tests;

public class NatsConnectionTestTcp : NatsConnectionTest
{
    public NatsConnectionTestTcp(ITestOutputHelper output) : base(output, TransportType.Tcp)
    {
    }
}

public class NatsConnectionTestWs : NatsConnectionTest
{
    public NatsConnectionTestWs(ITestOutputHelper output) : base(output, TransportType.WebSocket)
    {
    }
}
