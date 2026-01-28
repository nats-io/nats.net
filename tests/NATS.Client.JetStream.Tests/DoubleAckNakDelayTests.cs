using System.Text.RegularExpressions;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class DoubleAckNakDelayTests
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public DoubleAckNakDelayTests(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Double_ack_received_messages(NatsRequestReplyMode mode)
    {
        var proxy = _server.CreateProxy();
        await using var nats = proxy.CreateNatsConnection(mode);
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync($"{prefix}s1.foo", 42, cancellationToken: cts.Token);
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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Delay_nak_received_messages(NatsRequestReplyMode mode)
    {
        var proxy = _server.CreateProxy();
        await using var nats = proxy.CreateNatsConnection(mode);
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync($"{prefix}s1.foo", 42, cancellationToken: cts.Token);
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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Terminate_without_reason(NatsRequestReplyMode mode)
    {
        var proxy = _server.CreateProxy();
        await using var nats = proxy.CreateNatsConnection(mode);
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync($"{prefix}s1.foo", 42, cancellationToken: cts.Token);
        ack.EnsureSuccess();
        var next = await consumer.NextAsync<int>(cancellationToken: cts.Token);
        if (next is { } msg)
        {
            await msg.AckTerminateAsync(cancellationToken: cts.Token);
            Assert.Equal(42, msg.Data);

            await Retry.Until("seen TERM", () => proxy.Frames.Any(f => f.Message.StartsWith("PUB $JS.ACK")));

            var termFrame = proxy.Frames.Single(f => f.Message.StartsWith("PUB $JS.ACK"));

            Assert.Matches(@"\+TERM\s*$", termFrame.Message);
        }
        else
        {
            Assert.Fail("No message received");
        }
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Terminate_with_reason(NatsRequestReplyMode mode)
    {
        var proxy = _server.CreateProxy();
        await using var nats = proxy.CreateNatsConnection(mode);
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync($"{prefix}s1.foo", 42, cancellationToken: cts.Token);
        ack.EnsureSuccess();
        var next = await consumer.NextAsync<int>(cancellationToken: cts.Token);
        if (next is { } msg)
        {
            await msg.AckTerminateAsync(reason: "test failure reason", cancellationToken: cts.Token);
            Assert.Equal(42, msg.Data);

            await Retry.Until("seen TERM", () => proxy.Frames.Any(f => f.Message.StartsWith("PUB $JS.ACK")));

            var termFrame = proxy.Frames.Single(f => f.Message.StartsWith("PUB $JS.ACK"));

            Assert.Contains("+TERM test failure reason", termFrame.Message);
        }
        else
        {
            Assert.Fail("No message received");
        }
    }
}
