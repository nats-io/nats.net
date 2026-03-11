using System.Text.Json.Nodes;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using Synadia.Orbit.Testing.NatsServerProcessManager;

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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Create_push_consumer(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Consumer_config(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId();
        var js = new NatsJSContextFactory().CreateContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", [$"{prefix}s1.*"]), cts.Token);

        var consumerConfig = new ConsumerConfig
        {
            Name = $"{prefix}c1",
            Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)],
        };

        if (new Version("2.10") >= NatsServerExe.Version)
        {
            // nats-server 2.9 needs this to be set to > length of backoff
            // error: max deliver is required to be > length of backoff values
            consumerConfig.MaxDeliver = 4;
        }

        await js.CreateOrUpdateConsumerAsync(
            stream: $"{prefix}s1",
            config: consumerConfig,
            cancellationToken: cts.Token);

        // Check the consumer config
        var consumer = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        var info = consumer.Info;
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)], info.Config.Backoff);

        // Check the consumer config as JSON
        var response = await nats.RequestAsync<string>(
            subject: $"{js.Opts.Prefix}.CONSUMER.INFO.{prefix}s1.{prefix}c1",
            cancellationToken: cts.Token);
        var json = JsonNode.Parse(response.Data!);

        // _output.WriteLine($"JSON: {json}");
        Assert.NotNull(json);
        Assert.NotNull(json["config"]);

        // Nano seconds:       ms  us  ns
        const long seconds = 1_000_000_000;
        var backoff = json["config"]!["backoff"]!.AsArray().Select(x => x!.GetValue<long>()).ToArray();
        backoff.Should().BeEquivalentTo([1 * seconds, 2 * seconds, 3 * seconds]);
    }
}
