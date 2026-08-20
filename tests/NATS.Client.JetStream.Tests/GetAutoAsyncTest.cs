using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class GetAutoAsyncTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public GetAutoAsyncTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10.28")]
    public async Task GetAutoAsync_WithAllowDirectAndExistingSeq_ReturnsDirectMessage()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = await js.CreateStreamAsync(
            new StreamConfig($"{prefix}S1", [$"{prefix}s1"]) { AllowDirect = true },
            cancellationToken: cts.Token);

        await js.PublishAsync($"{prefix}s1", "hello-world", cancellationToken: cts.Token);

        var result = await stream.GetAutoAsync<string>(
            new StreamMsgGetRequest { Seq = 1 },
            cancellationToken: cts.Token);

        Assert.Equal("hello-world", result.Data);
        Assert.Equal(1UL, result.Sequence);
        Assert.Equal($"{prefix}s1", result.Subject);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10.28")]
    public async Task GetAutoAsync_WithAllowDirectAndMissingSeq_FallsBackToStreamGet()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = await js.CreateStreamAsync(
            new StreamConfig($"{prefix}S1", [$"{prefix}s1"]) { AllowDirect = true },
            cancellationToken: cts.Token);

        await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            await stream.GetAutoAsync<string>(
                new StreamMsgGetRequest { Seq = 999 },
                cancellationToken: cts.Token));
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10.28")]
    public async Task GetAutoAsync_WithoutAllowDirect_UsesStreamGet()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = await js.CreateStreamAsync(
            new StreamConfig($"{prefix}S1", [$"{prefix}s1"]) { AllowDirect = false },
            cancellationToken: cts.Token);

        await js.PublishAsync($"{prefix}s1", "hello-stream", cancellationToken: cts.Token);

        var result = await stream.GetAutoAsync<string>(
            new StreamMsgGetRequest { Seq = 1 },
            cancellationToken: cts.Token);

        Assert.Equal("hello-stream", result.Data);
        Assert.Equal(1UL, result.Sequence);
        Assert.Equal($"{prefix}s1", result.Subject);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10.28")]
    public async Task GetAutoAsync_WithLastBySubjAndAllowDirect_ReturnsDirectMessage()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = await js.CreateStreamAsync(
            new StreamConfig($"{prefix}S1", [$"{prefix}s1"]) { AllowDirect = true },
            cancellationToken: cts.Token);

        await js.PublishAsync($"{prefix}s1", "msg-1", cancellationToken: cts.Token);
        await js.PublishAsync($"{prefix}s1", "msg-2", cancellationToken: cts.Token);
        await js.PublishAsync($"{prefix}s1", "msg-3", cancellationToken: cts.Token);

        var result = await stream.GetAutoAsync<string>(
            new StreamMsgGetRequest { LastBySubj = $"{prefix}s1" },
            cancellationToken: cts.Token);

        Assert.Equal("msg-3", result.Data);
        Assert.Equal($"{prefix}s1", result.Subject);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10.28")]
    public async Task GetAutoAsync_WithLastBySubjMissing_FallsBackToStreamGet()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = await js.CreateStreamAsync(
            new StreamConfig($"{prefix}S1", [$"{prefix}s1"]) { AllowDirect = true },
            cancellationToken: cts.Token);

        await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            await stream.GetAutoAsync<string>(
                new StreamMsgGetRequest { LastBySubj = $"{prefix}nonexistent" },
                cancellationToken: cts.Token));
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10.28")]
    public async Task GetAutoAsync_WithAllowDirectAndMultipleMessages_ReturnsCorrectSequence()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = await js.CreateStreamAsync(
            new StreamConfig($"{prefix}S1", [$"{prefix}s1"]) { AllowDirect = true },
            cancellationToken: cts.Token);

        await js.PublishAsync($"{prefix}s1", "first", cancellationToken: cts.Token);
        await js.PublishAsync($"{prefix}s1", "second", cancellationToken: cts.Token);
        await js.PublishAsync($"{prefix}s1", "third", cancellationToken: cts.Token);

        var result = await stream.GetAutoAsync<string>(
            new StreamMsgGetRequest { Seq = 2 },
            cancellationToken: cts.Token);

        Assert.Equal("second", result.Data);
        Assert.Equal(2UL, result.Sequence);
        Assert.Equal($"{prefix}s1", result.Subject);
    }
}
