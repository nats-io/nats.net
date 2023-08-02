using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class ConsumerConsumeTest
{
    private readonly ITestOutputHelper _output;

    public ConsumerConsumeTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Consume_msgs_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = NatsServer.Start(
            outputHelper: _output,
            options: new NatsServerOptionsBuilder()
                .UseTransport(TransportType.Tcp)
                .Trace()
                .UseJetStream()
                .Build());

        var (nats, proxy) = server.CreateProxiedClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        for (var i = 0; i < 30; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumerOpts = new NatsJSConsumeOpts(maxMsgs: 10);
        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<TestData>(consumerOpts, cts.Token))
        {
            await msg.Ack(cts.Token);
            Assert.Equal(count, msg.Msg.Data!.Test);
            count++;
            if (count == 25)
                break;
        }

        Assert.Equal(25, count);

        // TODO: we seem to be getting inconsistent number of pulls here!
        // It's sometimes 5 sometimes 7!
        // await Retry.Until(
        //     "receiving all pulls",
        //     () => proxy
        //               .ClientFrames
        //               .Count(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1")) == 7);
        var msgNextRequests = proxy
            .ClientFrames
            .Where(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1"))
            .ToList();

        // Prefetch
        Assert.Matches(@"^PUB.*""batch"":10\b", msgNextRequests.First().Message);

        foreach (var frame in msgNextRequests.Skip(1))
        {
            // Consequent fetches should top up to the prefetch value
            Assert.Matches(@"^PUB.*""batch"":5\b", frame.Message);
        }
    }

    [Fact]
    public async Task Consume_idle_heartbeat_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = NatsServer.Start(
            outputHelper: new NullOutputHelper(),
            options: new NatsServerOptionsBuilder()
                .UseTransport(TransportType.Tcp)
                .UseJetStream()
                .Build());

        var (nats, proxy) = server.CreateProxiedClientConnection();

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync("s1.foo", new TestData { Test = 0 }, cancellationToken: cts.Token);
        ack.EnsureSuccess();

        var signal = new WaitSignal(TimeSpan.FromSeconds(30));
        var consumerOpts = new NatsJSConsumeOpts(
            maxMsgs: 10,
            idleHeartbeat: TimeSpan.FromSeconds(5),
            errorHandler: e =>
            {
                signal.Pulse();
            });
        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<TestData>(consumerOpts, cts.Token))
        {
            await msg.Ack(cts.Token);
            Assert.Equal(count, msg.Msg.Data!.Test);
            await signal;
            break;
        }

        var msgNextRequests = proxy
            .ClientFrames
            .Where(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1"))
            .ToList();

        Assert.Single(msgNextRequests);

        // Prefetch
        Assert.Matches(@"^PUB.*""batch"":10\b", msgNextRequests.First().Message);

        foreach (var frame in msgNextRequests.Skip(1))
        {
            // Consequent fetches should top up to the prefetch value
            Assert.Matches(@"^PUB.*""batch"":5\b", frame.Message);
        }
    }

    private record TestData
    {
        public int Test { get; init; }
    }
}
