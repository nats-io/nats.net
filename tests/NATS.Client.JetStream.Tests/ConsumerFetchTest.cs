using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class ConsumerFetchTest
{
    private readonly ITestOutputHelper _output;

    public ConsumerFetchTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Fetch_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await using var fc =
            await consumer.FetchAsync<TestData>(new NatsJSFetchOpts { MaxMsgs = 10 }, cancellationToken: cts.Token);
        await foreach (var msg in fc.Msgs.ReadAllAsync(cts.Token))
        {
            await msg.AckAsync(new AckOpts(WaitUntilSent: true), cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            count++;
        }

        Assert.Equal(10, count);
    }

    private record TestData
    {
        public int Test { get; init; }
    }
}
