using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class ConsumerConsumeTest
{
    private readonly ITestOutputHelper _output;

    public ConsumerConsumeTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Consume_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10000));
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

        var msgNextRequests = proxy
            .ClientFrames
            .Where(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1"))
            .ToList();

        Assert.Equal(5, msgNextRequests.Count);

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
