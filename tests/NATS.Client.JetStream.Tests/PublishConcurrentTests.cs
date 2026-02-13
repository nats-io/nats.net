using System.Diagnostics;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class PublishConcurrentTests
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public PublishConcurrentTests(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Publish_concurrently()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"]);

        // Standard publish
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", 1);
            Assert.Null(ack.Error);
            Assert.Equal(1, (int)ack.Seq);
            Assert.Equal($"{prefix}s1", ack.Stream);
            Assert.False(ack.Duplicate);
            Assert.True(ack.IsSuccess());
            _output.WriteLine($"Published: {ack}");
        }

        // Concurrently publish
        {
            await using var future = await js.PublishConcurrentAsync($"{prefix}s1.foo", 2);
            var ack = await future.GetResponseAsync();
            _output.WriteLine($"Published: {ack}");
        }

        // Compare the performance
        var stopwatch1 = Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo.single", i);
            ack.EnsureSuccess();
        }

        _output.WriteLine($"PublishAsync: {stopwatch1.Elapsed}");

        // Concurrently, publish a batch
        var stopwatch2 = Stopwatch.StartNew();
        var futures = new NatsJSPublishConcurrentFuture[1_000];
        for (var i = 0; i < 1_000; i++)
        {
            futures[i] = await js.PublishConcurrentAsync($"{prefix}s1.foo.concurrent", i);
        }

        for (var i = 0; i < 1_000; i++)
        {
            await using var future = futures[i];
            var ack = await future.GetResponseAsync();
            ack.EnsureSuccess();
        }

        _output.WriteLine($"PublishConcurrentAsync: {stopwatch2.Elapsed}");

        Assert.True(stopwatch1.Elapsed > stopwatch2.Elapsed);

        await Retry.Until(
            "stream count settles down",
            async () => (await js.GetStreamAsync($"{prefix}s1")).Info.State.Messages == 2 + 1_000 + 1_000);
    }
}
