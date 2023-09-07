using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class PublishTest
{
    private readonly ITestOutputHelper _output;

    public PublishTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Publish_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        // Publish
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = 1 }, cancellationToken: cts.Token);
            Assert.Null(ack.Error);
            Assert.Equal(1, ack.Seq);
            Assert.Equal("s1", ack.Stream);
            Assert.False(ack.Duplicate);
        }

        // Duplicate
        {
            var ack1 = await js.PublishAsync(
                subject: "s1.foo",
                data: new TestData { Test = 2 },
                headers: new NatsHeaders { { "Nats-Msg-Id", "2" } },
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);
            Assert.Equal(2, ack1.Seq);
            Assert.False(ack1.Duplicate);

            var ack2 = await js.PublishAsync(
                subject: "s1.foo",
                data: new TestData { Test = 2 },
                headers: new NatsHeaders { { "Nats-Msg-Id", "2" } },
                cancellationToken: cts.Token);
            Assert.Null(ack2.Error);
            Assert.True(ack2.Duplicate);
        }
    }

    private record TestData
    {
        public int Test { get; init; }
    }
}
