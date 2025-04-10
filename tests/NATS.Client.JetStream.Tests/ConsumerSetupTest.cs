using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.Platform.Windows.Tests;
using NATS.Client.TestUtilities;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ConsumerSetupTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public ConsumerSetupTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Create_push_consumer()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        await js.CreateOrUpdateConsumerAsync(
            stream: $"{prefix}s1",
            config: new ConsumerConfig
            {
                Name = $"{prefix}c1",
                DeliverSubject = $"{prefix}i1",
                DeliverGroup = $"{prefix}q1",
            },
            cancellationToken: cts.Token);

        var consumer = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);

        var info = consumer.Info;
        Assert.Equal($"{prefix}s1", info.StreamName);

        var config = info.Config;
        Assert.Equal($"{prefix}c1", config.Name);
        Assert.Equal($"{prefix}i1", config.DeliverSubject);
        Assert.Equal($"{prefix}q1", config.DeliverGroup);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Create_paused_consumer()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js = new NatsJSContextFactory().CreateContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", [$"{prefix}s1.*"]), cts.Token);

        var pauseUntil = DateTimeOffset.Now.AddHours(1);

        await js.CreateOrUpdateConsumerAsync(
            stream: $"{prefix}s1",
            config: new ConsumerConfig
            {
                Name = $"{prefix}c1",
                PauseUntil = pauseUntil,
            },
            cancellationToken: cts.Token);

        var consumer = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);

        var info = consumer.Info;
        Assert.True(info.IsPaused);

        var config = info.Config;
        Assert.Equal(pauseUntil, config.PauseUntil);
    }
}
