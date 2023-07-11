using System.Buffers;
using System.Text;

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
            () => proxy.ClientFrames.Count(f => f.Message.StartsWith("UNSUB")) == 1,
            async () => GC.Collect(),
            retryDelay: TimeSpan.FromSeconds(.5));
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

    [Fact]
    public async Task Auto_unsubscribe_test()
    {
        // Use a single server to test multiple scenarios to make test runs more efficient
        await using var server = new NatsServer();
        await using var nats = server.CreateClientConnection();

        // Auto unsubscribe on max messages
        {
            const string subject = "foo1";
            const int maxMsgs = 99;
            var opts = new NatsSubOpts { MaxMsgs = maxMsgs };

            await using var sub = await nats.SubscribeAsync<int>(subject, opts);

            // send more messages than max to check we only get max
            for (var i = 0; i < maxMsgs + 10; i++)
            {
                await nats.PublishAsync(subject, i);
            }

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancellationToken = cts.Token;
            var count = 0;
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync(cancellationToken))
            {
                Assert.Equal(count, natsMsg.Data);
                count++;
            }

            Assert.Equal(maxMsgs, count);
            Assert.Equal(NatsSubEndReason.MaxMsgs, sub.EndReason);
        }

        // Auto unsubscribe on timeout
        {
            const string subject = "foo2";
            var opts = new NatsSubOpts { Timeout = TimeSpan.FromSeconds(1) };

            await using var sub = await nats.SubscribeAsync<int>(subject, opts);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancellationToken = cts.Token;
            var count = 0;
            await foreach (var unused in sub.Msgs.ReadAllAsync(cancellationToken))
            {
                count++;
            }

            Assert.Equal(NatsSubEndReason.Timeout, sub.EndReason);
            Assert.Equal(0, count);
        }

        // Auto unsubscribe on idle timeout
        {
            const string subject = "foo3";
            var opts = new NatsSubOpts { IdleTimeout = TimeSpan.FromSeconds(2) };

            await using var sub = await nats.SubscribeAsync<int>(subject, opts);

            await nats.PublishAsync(subject, 0);
            await nats.PublishAsync(subject, 1);
            await nats.PublishAsync(subject, 2);
            await Task.Delay(TimeSpan.FromSeconds(.1));
            await nats.PublishAsync(subject, 3);
            await Task.Delay(TimeSpan.FromSeconds(2.1));
            await nats.PublishAsync(subject, 100);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancellationToken = cts.Token;
            var count = 0;
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync(cancellationToken))
            {
                Assert.Equal(count, natsMsg.Data);
                count++;
            }

            Assert.Equal(NatsSubEndReason.IdleTimeout, sub.EndReason);
            Assert.Equal(4, count);
        }

        // Manual unsubscribe
        {
            const string subject = "foo4";
            await using var sub = await nats.SubscribeAsync<int>(subject);

            await sub.UnsubscribeAsync();

            for (var i = 0; i < 10; i++)
            {
                await nats.PublishAsync(subject, i);
            }

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancellationToken = cts.Token;
            var count = 0;
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync(cancellationToken))
            {
                Assert.Equal(count, natsMsg.Data);
                count++;
            }

            Assert.Equal(0, count);
            Assert.Equal(NatsSubEndReason.None, sub.EndReason);
        }
    }
}
