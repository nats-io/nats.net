using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class PublishTest
{
    private class TestData
    {
        public int Test { get; set; }
    }

    private readonly ITestOutputHelper _output;

    public PublishTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Publish_test()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var streams = new NatsJSManageStreams(js);
        var consumers = new NatsJSManageConsumers(js);
        await streams.CreateAsync("s1", "s1.*");
        await consumers.CreateAsync("s1", "c1");

        // Publish
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = 1 });
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
                opts: new NatsPubOpts { Headers = new NatsHeaders { { "Nats-Msg-Id", "2" } } });
            Assert.Null(ack1.Error);
            Assert.Equal(2, ack1.Seq);
            Assert.False(ack1.Duplicate);

            var ack2 = await js.PublishAsync(
                subject: "s1.foo",
                data: new TestData { Test = 2 },
                opts: new NatsPubOpts { Headers = new NatsHeaders { { "Nats-Msg-Id", "2" } } });
            Assert.Null(ack2.Error);
            Assert.True(ack2.Duplicate);
        }
    }
}
