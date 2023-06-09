namespace NATS.Client.Core.Tests;

public class SubscriptionTest
{
    private readonly ITestOutputHelper _output;

    public SubscriptionTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Subscription_with_same_subject()
    {
        _output.WriteLine($"### [Subscription_with_same_subject] started");
        await using var server = new NatsServer(_output, TransportType.Tcp, new NatsServerOptions { UseEphemeralPort = true });

        _output.WriteLine($"### [Subscription_with_same_subject] publisher connection");
        var conn1 = server.CreateClientConnection();

        _output.WriteLine($"### [Subscription_with_same_subject] subscriber connection");
        var (conn2, tap) = server.CreateTappedClientConnection();

        var sub1 = await conn2.SubscribeAsync<int>("foo.bar");
        var sub2 = await conn2.SubscribeAsync<int>("foo.bar");
        var sub3 = await conn2.SubscribeAsync<int>("foo.baz");
        _output.WriteLine($"### [Subscription_with_same_subject] subscribers created");

        await conn1.PingAsync();
        await conn2.PingAsync();
        _output.WriteLine($"### [Subscription_with_same_subject] pinged");

        await conn1.PublishAsync("foo.bar", 1);
        await conn1.PublishAsync("foo.baz", 2);
        _output.WriteLine($"### [Subscription_with_same_subject] published");

        var count = new WaitSignal(TimeSpan.FromSeconds(30), 3);
        sub1.Register(m =>
        {
            _output.WriteLine($"### [Subscription_with_same_subject] sub1 rcv");
            count.Pulse(m.Subject == "foo.bar" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });
        sub2.Register(m =>
        {
            _output.WriteLine($"### [Subscription_with_same_subject] sub2 rcv");
            count.Pulse(m.Subject == "foo.bar" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });
        sub3.Register(m =>
        {
            _output.WriteLine($"### [Subscription_with_same_subject] sub3 rcv");
            count.Pulse(m.Subject == "foo.baz" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });
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

        await sub1.DisposeAsync();
        await sub2.DisposeAsync();
        await sub3.DisposeAsync();
        await conn1.DisposeAsync();
        await conn2.DisposeAsync();
        tap.Dispose();
        _output.WriteLine($"### [Subscription_with_same_subject] done");
    }
}
