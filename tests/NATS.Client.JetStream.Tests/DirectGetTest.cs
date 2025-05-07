using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class DirectGetTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public DirectGetTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10.28")]
    public async Task Direct_get_returns_503_no_responders()
    {
        // https://github.com/nats-io/nats-server/commit/ce309b79d99552996e18dce47dc04bdc730c0d84
        // When we fail to deliver a message through a service import respond with no responders.
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var s1 = await js.CreateStreamAsync(
            new StreamConfig($"{prefix}S1", [$"{prefix}s1"]) { AllowDirect = false },
            cancellationToken: cts.Token);

        await js.PublishAsync($"{prefix}s1", "x", cancellationToken: cts.Token);

        await Assert.ThrowsAsync<NatsNoRespondersException>(async () => await s1.GetDirectAsync<string>(
            new StreamMsgGetRequest { Seq = 1 },
            cancellationToken: cts.Token));
    }

    [SkipIfNatsServer(versionLaterThan: "2.10.28")]
    public async Task Direct_get_returns_no_reply()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var s1 = await js.CreateStreamAsync(
            new StreamConfig($"{prefix}S1", [$"{prefix}s1"]) { AllowDirect = false },
            cancellationToken: cts.Token);

        await js.PublishAsync($"{prefix}s1", "x", cancellationToken: cts.Token);

        await Assert.ThrowsAsync<NatsNoReplyException>(async () => await s1.GetDirectAsync<string>(
            new StreamMsgGetRequest { Seq = 1 },
            cancellationToken: cts.Token));
    }
}
