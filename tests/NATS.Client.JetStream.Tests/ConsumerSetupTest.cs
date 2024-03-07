using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ConsumerSetupTest
{
    [Fact]
    public async Task Create_push_consumer()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        await js.CreateOrUpdateConsumerAsync(
            stream: "s1",
            config: new ConsumerConfig
            {
                Name = "c1",
                DeliverSubject = "i1",
                DeliverGroup = "q1",
            },
            cancellationToken: cts.Token);

        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);

        var info = consumer.Info;
        Assert.Equal("s1", info.StreamName);

        var config = info.Config;
        Assert.Equal("c1", config.Name);
        Assert.Equal("i1", config.DeliverSubject);
        Assert.Equal("q1", config.DeliverGroup);
    }

    [Fact]
    public async Task Create_paused_consumer()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        var pauseUntil = DateTimeOffset.Now.AddHours(1);

        await js.CreateOrUpdateConsumerAsync(
            stream: "s1",
            config: new ConsumerConfig
            {
                Name = "c1",
                PauseUntil = pauseUntil,
            },
            cancellationToken: cts.Token);

        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);

        var info = consumer.Info;
        Assert.True(info.IsPaused);

        var config = info.Config;
        Assert.Equal(pauseUntil, config.PauseUntil);
    }
}
