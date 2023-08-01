using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class ConsumerFetchTest
{
    private readonly ITestOutputHelper _output;

    public ConsumerFetchTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Fetch_test()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", "s1.*");
        await js.CreateConsumerAsync("s1", "c1");
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await foreach (var msg in consumer.FetchAsync<TestData>(10, cts.Token))
        {
            await msg.Ack(cts.Token);
            Assert.Equal(count, msg.Msg.Data!.Test);
            count++;
        }

        Assert.Equal(10, count);
    }

    private record TestData
    {
        public int Test { get; init; }
    }
}
