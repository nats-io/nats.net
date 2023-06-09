using System.Text.RegularExpressions;

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

        var sync1 = 0;
        var sync2 = 0;
        var sync3 = 0;
        var count = new WaitSignal(3);

        sub1.Register(m =>
        {
            if (m.Data == 0)
            {
                Interlocked.Exchange(ref sync1, 1);
                return;
            }

            count.Pulse(m.Subject == "foo.bar" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });

        sub2.Register(m =>
        {
            if (m.Data == 0)
            {
                Interlocked.Exchange(ref sync2, 1);
                return;
            }

            count.Pulse(m.Subject == "foo.bar" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });

        sub3.Register(m =>
        {
            if (m.Data == 0)
            {
                Interlocked.Exchange(ref sync3, 1);
                return;
            }

            count.Pulse(m.Subject == "foo.baz" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });

        // Wait until all subscriptions are active
        await Task.Run(async () =>
        {
            while (Volatile.Read(ref sync1) + Volatile.Read(ref sync2) + Volatile.Read(ref sync3) != 3)
            {
                await Task.Delay(100);
                await conn1.PublishAsync("foo.bar", 0);
                await conn1.PublishAsync("foo.baz", 0);
            }
        });

        await conn1.PublishAsync("foo.bar", 1);
        await conn1.PublishAsync("foo.baz", 1);

        // Wait until we received all test data
        await count;

        var frames = tap.ClientFrames.OrderBy(f => f.Message).ToList();

        foreach (var frame in frames)
        {
            _output.WriteLine($"[TAP] {frame}");
        }

        Assert.Equal(3, frames.Count);
        Assert.StartsWith("SUB foo.bar", frames[0].Message);
        Assert.StartsWith("SUB foo.bar", frames[1].Message);
        Assert.StartsWith("SUB foo.baz", frames[2].Message);
        Assert.False(frames[0].Message.Equals(frames[1].Message), "Should have different SIDs");

        await sub1.DisposeAsync();
        await sub2.DisposeAsync();
        await sub3.DisposeAsync();
        await conn1.DisposeAsync();
        await conn2.DisposeAsync();
        tap.Dispose();
    }
}
