using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ConsumerNextTest
{
    private record TestData
    {
        public int Test { get; set; }
    }

    private readonly ITestOutputHelper _output;

    public ConsumerNextTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Next_test()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var streams = new NatsJSManageStreams(js);
        var consumers = new NatsJSManageConsumers(js);
        await streams.CreateAsync("s1", "s1.*");
        var consumerInfo = await consumers.CreateAsync("s1", "c1");
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var consumer = new NatsJSConsumer(js, consumerInfo, new ConsumerOpts());

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
            var msg = await consumer.NextAsync<TestData>(cts.Token);
            await msg.Ack(cts.Token);
            Assert.Equal(i, msg.Msg.Data!.Test);
        }
    }
}
