using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class ConsumerNextTest
{
    private readonly ITestOutputHelper _output;

    public ConsumerNextTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Next_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        var consumer = await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
            var next = await consumer.NextAsync<TestData>(new NatsJSNextOpts(), cts.Token);
            if (next is { } msg)
            {
                await msg.AckAsync(new AckOpts(WaitUntilSent: true), cts.Token);
                Assert.Equal(i, msg.Data!.Test);
            }
        }
    }

    private record TestData
    {
        public int Test { get; init; }
    }
}
