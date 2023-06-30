namespace NATS.Client.Core.Tests;

public class SubscriptionTest
{
    private readonly ITestOutputHelper _output;

    public SubscriptionTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Subscription_periodic_cleanup_test()
    {
        await using var server = new NatsServer(_output, TransportType.Tcp);
        var options = NatsOptions.Default with { SubscriptionCleanUpInterval = TimeSpan.FromSeconds(1) };
        var (nats, proxy) = server.CreateProxiedClientConnection(options);

        async Task Isolator()
        {
            var sub = await nats.SubscribeAsync<int>("foo");

            await Retry.Until(
                "unsubscribed",
                () => proxy.ClientFrames.Count(f => f.Message.StartsWith("SUB")) == 1);

            // subscription object will be eligible for GC after next statement
            Assert.Equal("foo", sub.Subject);
        }

        await Isolator();

        GC.Collect();

        await Retry.Until(
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

            await Retry.Until("unsubscribed", () => proxy.ClientFrames.Count(f => f.Message.StartsWith("SUB")) == 1);

            // subscription object will be eligible for GC after next statement
            Assert.Equal("foo", sub.Subject);
        }

        await Isolator();

        GC.Collect();

        // Publish should trigger UNSUB since NatsSub object should be collected by now.
        await Retry.Until(
            "unsubscribe message received",
            () => proxy.ClientFrames.Count(f => f.Message.StartsWith("UNSUB")) == 1,
            async () =>
            {
                GC.Collect();
                await nats.PublishAsync("foo", 1);
            },
            timeout: TimeSpan.FromSeconds(30),
            retryDelay: TimeSpan.FromSeconds(.5));
    }
}
