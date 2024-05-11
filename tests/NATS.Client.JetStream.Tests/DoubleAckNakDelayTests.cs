using System.Text.RegularExpressions;
using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class DoubleAckNakDelayTests
{
    private readonly ITestOutputHelper _output;

    public DoubleAckNakDelayTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Double_ack_received_messages()
    {
        await using var server = NatsServer.StartJS();
        var (nats1, proxy) = server.CreateProxiedClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
        await using var nats = nats1;
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync("s1.foo", 42, cancellationToken: cts.Token);
        ack.EnsureSuccess();
        var next = await consumer.NextAsync<int>(cancellationToken: cts.Token);
        if (next is { } msg)
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Assert.Equal(42, msg.Data);

            await Retry.Until("seen ACK", () => proxy.Frames.Any(f => f.Message.StartsWith("PUB $JS.ACK")));

            var ackFrame = proxy.Frames.Single(f => f.Message.StartsWith("PUB $JS.ACK"));
            var inbox = Regex.Match(ackFrame.Message, @"\s(_INBOX\.\w+\.\w+)\s+\d").Groups[1].Value;

            await Retry.Until("seen ACK-ACK", () => proxy.Frames.Any(f => f.Message.StartsWith($"MSG {inbox}")));
        }
        else
        {
            Assert.Fail("No message received");
        }
    }

    [Fact]
    public async Task Delay_nak_received_messages()
    {
        await using var server = NatsServer.StartJS();
        var (nats1, proxy) = server.CreateProxiedClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
        await using var nats = nats1;
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync("s1.foo", 42, cancellationToken: cts.Token);
        ack.EnsureSuccess();
        var next = await consumer.NextAsync<int>(cancellationToken: cts.Token);
        if (next is { } msg)
        {
            await msg.NakAsync(delay: TimeSpan.FromSeconds(123), cancellationToken: cts.Token);
            Assert.Equal(42, msg.Data);

            await Retry.Until("seen ACK", () => proxy.Frames.Any(f => f.Message.StartsWith("PUB $JS.ACK")));

            var nakFrame = proxy.Frames.Single(f => f.Message.StartsWith("PUB $JS.ACK"));

            Assert.Matches(@"-NAK\s+\{\s*""delay""\s*:\s*123000000000\s*\}", nakFrame.Message);
        }
        else
        {
            Assert.Fail("No message received");
        }
    }
}
