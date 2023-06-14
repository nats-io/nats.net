using System.Diagnostics;

namespace NATS.Client.Core.Tests;

public class SubscriptionTest
{
    private readonly ITestOutputHelper _output;

    public SubscriptionTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Subscription_with_same_subject()
    {
        await using var server = new NatsServer(_output, TransportType.Tcp);
        var nats1 = server.CreateClientConnection();
        var (nats2, proxy) = server.CreateProxiedClientConnection();

        var sub1 = await nats2.SubscribeAsync<int>("foo.bar");
        var sub2 = await nats2.SubscribeAsync<int>("foo.bar");
        var sub3 = await nats2.SubscribeAsync<int>("foo.baz");

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

        // Since subscription and publishing are sent through different connections there is
        // a race where one or more subscriptions are made after the publishing happens.
        // So, we make sure subscribers are accepted by the server before we send any test data.
        await RetryUntil(
            "all subscriptions are active",
            () => Volatile.Read(ref sync1) + Volatile.Read(ref sync2) + Volatile.Read(ref sync3) == 3,
            async () =>
            {
                await nats1.PublishAsync("foo.bar", 0);
                await nats1.PublishAsync("foo.baz", 0);
            });

        await nats1.PublishAsync("foo.bar", 1);
        await nats1.PublishAsync("foo.baz", 1);

        // Wait until we received all test data
        await count;

        var frames = proxy.ClientFrames.OrderBy(f => f.Message).ToList();

        foreach (var frame in frames)
        {
            _output.WriteLine($"[PROXY] {frame}");
        }

        Assert.Equal(3, frames.Count);
        Assert.StartsWith("SUB foo.bar", frames[0].Message);
        Assert.StartsWith("SUB foo.bar", frames[1].Message);
        Assert.StartsWith("SUB foo.baz", frames[2].Message);
        Assert.False(frames[0].Message.Equals(frames[1].Message), "Should have different SIDs");

        await sub1.DisposeAsync();
        await sub2.DisposeAsync();
        await sub3.DisposeAsync();
        await nats1.DisposeAsync();
        await nats2.DisposeAsync();
        proxy.Dispose();
    }

    [Fact]
    public async Task Subscription_periodic_cleanup_test()
    {
        await using var server = new NatsServer(_output, TransportType.Tcp);
        var options = NatsOptions.Default with { SubscriptionCleanUpInterval = TimeSpan.FromSeconds(1) };
        var (nats, proxy) = server.CreateProxiedClientConnection(options);

        async Task Isolator()
        {
            var sub = await nats.SubscribeAsync<int>("foo");

            await RetryUntil(
                "unsubscribed",
                () => proxy.ClientFrames.Count(f => f.Message.StartsWith("SUB")) == 1);

            // subscription object will be eligible for GC after next statement
            Assert.Equal("foo", sub.Subject);
        }

        await Isolator();

        GC.Collect();

        await RetryUntil(
            "unsubscribe message received",
            () => proxy.ClientFrames.Count(f => f.Message.StartsWith("UNSUB")) == 1);
    }

    [Fact]
    public async Task Subscription_cleanup_on_message_receive_test()
    {
        await using var server = new NatsServer(_output, TransportType.Tcp);

        // Make sure time won't kick-in and unsubscribe
        var options = NatsOptions.Default with { SubscriptionCleanUpInterval = TimeSpan.MaxValue };
        var (nats, proxy) = server.CreateProxiedClientConnection(options);

        async Task Isolator()
        {
            var sub = await nats.SubscribeAsync<int>("foo");

            await RetryUntil("unsubscribed", () => proxy.ClientFrames.Count(f => f.Message.StartsWith("SUB")) == 1);

            // subscription object will be eligible for GC after next statement
            Assert.Equal("foo", sub.Subject);
        }

        await Isolator();

        GC.Collect();

        // Should trigger UNSUB since NatsSub object should be collected by now.
        await nats.PublishAsync("foo", 1);

        await RetryUntil(
            "unsubscribe message received",
            () => proxy.ClientFrames.Count(f => f.Message.StartsWith("UNSUB")) == 1);
    }

    private async Task RetryUntil(string reason, Func<bool> condition, Func<Task>? action = null, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (action != null)
                await action();
            if (condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Took too long ({timeout}) waiting for {reason}");
    }
}
