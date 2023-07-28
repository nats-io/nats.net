using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ConsumerFetchTest
{
    private record TestData
    {
        public int Test { get; set; }
    }

    private readonly ITestOutputHelper _output;

    public ConsumerFetchTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Fetch_test()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var streams = new NatsJSManageStreams(js);
        var consumers = new NatsJSManageConsumers(js);
        await streams.CreateAsync("s1", "s1.*");
        var consumerInfo = await consumers.CreateAsync("s1", "c1");
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = new NatsJSConsumer(js, consumerInfo, new ConsumerOpts());
        var count = 0;
        await foreach (var msg in consumer.FetchAsync<TestData>(10, cts.Token))
        {
            await msg.Ack(cts.Token);
            Assert.Equal(count, msg.Msg.Data!.Test);
            count++;
        }

        Assert.Equal(10, count);
    }
}
