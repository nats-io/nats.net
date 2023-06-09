namespace NATS.Client.Core.Tests;

public class SubscriptionTest
{
    private readonly ITestOutputHelper _output;

    public SubscriptionTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Subscription_with_same_subject()
    {
        await using var server = new NatsServer(_output, TransportType.Tcp, new NatsServerOptions { UseEphemeralPort = true });
        var conn1 = server.CreateClientConnection();
        var (conn2, tap) = server.CreateTappedClientConnection();

        var sub1 = await conn2.SubscribeAsync<int>("foo.bar");
        var sub2 = await conn2.SubscribeAsync<int>("foo.bar");
        var sub3 = await conn2.SubscribeAsync<int>("foo.baz");

        await conn1.PingAsync();
        await conn2.PingAsync();

        await conn1.PublishAsync("foo.bar", 1);
        await conn1.PublishAsync("foo.baz", 2);

        var count = new WaitSignal(3);
        sub1.Register(_ => count.Pulse());
        sub2.Register(_ => count.Pulse());
        sub3.Register(_ => count.Pulse());
        await count;

        var frames = tap.ClientFrames;

        foreach (var frame in frames)
        {
            _output.WriteLine($"[TAP] {frame}");
        }

        Assert.Equal(3, frames.Count);
        Assert.StartsWith("SUB foo.bar", frames[0].Message);
        Assert.StartsWith("SUB foo.bar", frames[1].Message);
        Assert.StartsWith("SUB foo.baz", frames[2].Message);

        await conn1.DisposeAsync();
        await conn2.DisposeAsync();
        tap.Dispose();
    }
}
