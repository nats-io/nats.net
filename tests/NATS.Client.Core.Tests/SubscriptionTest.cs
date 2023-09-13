namespace NATS.Client.Core.Tests;

public class SubscriptionTest
{
    private readonly ITestOutputHelper _output;

    public SubscriptionTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Subscription_periodic_cleanup_test()
    {
        var serverOptions = new NatsServerOptsBuilder()
            .Trace()
            .UseTransport(TransportType.Tcp)
            .Build();
        await using var server = NatsServer.Start(_output, serverOptions);
        var options = NatsOpts.Default with { SubscriptionCleanUpInterval = TimeSpan.FromSeconds(1) };
        var (nats, proxy) = server.CreateProxiedClientConnection(options);

        async Task Isolator()
        {
            var sub = await nats.SubscribeAsync<int>("foo");

            await Retry.Until(
                reason: "unsubscribed",
                condition: () => proxy.ClientFrames.Count(f => f.Message.StartsWith("SUB")) == 1,
                retryDelay: TimeSpan.FromSeconds(.5),
                timeout: TimeSpan.FromSeconds(20));

            // subscription object will be eligible for GC after next statement
            Assert.Equal("foo", sub.Subject);
        }

        await Isolator();

        GC.Collect();

        await Retry.Until(
            reason: "unsubscribe message received",
            condition: () => proxy.ClientFrames.Count(f => f.Message.StartsWith("UNSUB")) >= 1,
            action: () =>
            {
                GC.Collect();
                return Task.CompletedTask;
            },
            retryDelay: TimeSpan.FromSeconds(.5),
            timeout: TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Subscription_cleanup_on_message_receive_test()
    {
        await using var server = NatsServer.Start(_output, TransportType.Tcp);

        // Make sure time won't kick-in and unsubscribe
        var options = NatsOpts.Default with { SubscriptionCleanUpInterval = TimeSpan.MaxValue };
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
            reason: "unsubscribe message received",
            condition: () => proxy.ClientFrames.Count(f => f.Message.StartsWith("UNSUB")) >= 1,
            action: async () =>
            {
                GC.Collect();
                await nats.PublishAsync("foo", 1);
            },
            timeout: TimeSpan.FromSeconds(20),
            retryDelay: TimeSpan.FromSeconds(.5));
    }

    [Fact]
    public async Task Auto_unsubscribe_on_max_messages_with_inbox_subscription_test()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        var subject = nats.NewInbox();

        await using var sub1 = await nats.SubscribeAsync<int>(subject, opts: new NatsSubOpts { MaxMsgs = 1 });
        await using var sub2 = await nats.SubscribeAsync<int>(subject, opts: new NatsSubOpts { MaxMsgs = 2 });

        for (var i = 0; i < 3; i++)
        {
            await nats.PublishAsync(subject, i);
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10000));
        var cancellationToken = cts.Token;

        var count1 = 0;
        await foreach (var natsMsg in sub1.Msgs.ReadAllAsync(cancellationToken))
        {
            Assert.Equal(count1, natsMsg.Data);
            count1++;
        }

        Assert.Equal(1, count1);
        Assert.Equal(NatsSubEndReason.MaxMsgs, ((NatsSubBase)sub1).EndReason);

        var count2 = 0;
        await foreach (var natsMsg in sub2.Msgs.ReadAllAsync(cancellationToken))
        {
            Assert.Equal(count2, natsMsg.Data);
            count2++;
        }

        Assert.Equal(2, count2);
        Assert.Equal(NatsSubEndReason.MaxMsgs, ((NatsSubBase)sub2).EndReason);
    }

    [Fact]
    public async Task Auto_unsubscribe_on_max_messages_test()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        const string subject = "foo1";
        const int maxMsgs = 99;
        var opts = new NatsSubOpts { MaxMsgs = maxMsgs };

        await using var sub = await nats.SubscribeAsync<int>(subject, opts: opts);

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
        Assert.Equal(NatsSubEndReason.MaxMsgs, ((NatsSubBase)sub).EndReason);
    }

    [Fact]
    public async Task Auto_unsubscribe_on_timeout_test()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        const string subject = "foo2";
        var opts = new NatsSubOpts { Timeout = TimeSpan.FromSeconds(1) };

        await using var sub = await nats.SubscribeAsync<int>(subject, opts: opts);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var count = 0;
        await foreach (var unused in sub.Msgs.ReadAllAsync(cancellationToken))
        {
            count++;
        }

        Assert.Equal(NatsSubEndReason.Timeout, ((NatsSubBase)sub).EndReason);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Auto_unsubscribe_on_idle_timeout_test()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        const string subject = "foo3";
        var opts = new NatsSubOpts { IdleTimeout = TimeSpan.FromSeconds(3) };

        await using var sub = await nats.SubscribeAsync<int>(subject, opts: opts);

        await nats.PublishAsync(subject, 0);
        await nats.PublishAsync(subject, 1);
        await nats.PublishAsync(subject, 2);
        await Task.Delay(TimeSpan.FromSeconds(.1));
        await nats.PublishAsync(subject, 3);
        await Task.Delay(TimeSpan.FromSeconds(5));
        await nats.PublishAsync(subject, 100);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var count = 0;
        await foreach (var natsMsg in sub.Msgs.ReadAllAsync(cancellationToken))
        {
            Assert.Equal(count, natsMsg.Data);
            count++;
        }

        Assert.Equal(NatsSubEndReason.IdleTimeout, ((NatsSubBase)sub).EndReason);
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Manual_unsubscribe_test()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
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
        Assert.Equal(NatsSubEndReason.None, ((NatsSubBase)sub).EndReason);
    }
}
