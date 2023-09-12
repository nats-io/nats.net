using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class ConsumerConsumeTest
{
    private readonly ITestOutputHelper _output;

    public ConsumerConsumeTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Consume_msgs_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var server = NatsServer.Start(
            outputHelper: _output,
            opts: new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tcp)
                .Trace()
                .UseJetStream()
                .Build());
        var (nats, proxy) = server.CreateProxiedClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        for (var i = 0; i < 30; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumerOpts = new NatsJSConsumeOpts { MaxMsgs = 10 };
        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await using var cc = await consumer.ConsumeAsync<TestData>(consumerOpts, cancellationToken: cts.Token);
        await foreach (var msg in cc.Msgs.ReadAllAsync(cts.Token))
        {
            await msg.AckAsync(new AckOpts(true), cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            count++;
            if (count == 30)
                break;
        }

        int? PullCount() => proxy?
            .ClientFrames
            .Count(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1"));

        await Retry.Until(
            reason: "received enough pulls",
            condition: () => PullCount() > 5,
            action: () =>
            {
                _output.WriteLine($"### PullCount:{PullCount()}");
                return Task.CompletedTask;
            },
            retryDelay: TimeSpan.FromSeconds(3),
            timeout: TimeSpan.FromSeconds(15));

        var msgNextRequests = proxy
            .ClientFrames
            .Where(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1"))
            .ToList();

        foreach (var frame in msgNextRequests)
        {
            var match = Regex.Match(frame.Message, @"^PUB.*""batch"":(\d+)");
            Assert.True(match.Success);
            var batch = int.Parse(match.Groups[1].Value);
            Assert.True(batch <= 10);
        }
    }

    [Fact]
    public async Task Consume_idle_heartbeat_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = NatsServer.StartJS();

        var (nats, proxy) = server.CreateProxiedClientConnection();

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync("s1.foo", new TestData { Test = 0 }, cancellationToken: cts.Token);
        ack.EnsureSuccess();

        var signal = new WaitSignal(TimeSpan.FromSeconds(30));
        server.OnLog += log =>
        {
            if (log is { Category: "NATS.Client.JetStream.Internal.NatsJSConsume", LogLevel: LogLevel.Debug })
            {
                if (log.EventId == NatsJSLogEvents.IdleTimeout)
                    signal.Pulse();
            }
        };
        var consumerOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
            IdleHeartbeat = TimeSpan.FromSeconds(5),
        };
        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        var cc = await consumer.ConsumeAsync<TestData>(consumerOpts, cancellationToken: cts.Token);
        await foreach (var msg in cc.Msgs.ReadAllAsync(cts.Token))
        {
            await msg.AckAsync(new AckOpts(WaitUntilSent: true), cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            await signal;
            break;
        }

        await Retry.Until(
            "all pull requests are received",
            () => proxy.ClientFrames.Count(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1")) == 2);

        var msgNextRequests = proxy
            .ClientFrames
            .Where(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1"))
            .ToList();

        Assert.Equal(2, msgNextRequests.Count);

        // Pull requests
        foreach (var frame in msgNextRequests)
        {
            Assert.Matches(@"^PUB.*""batch"":10\b", frame.Message);
        }
    }

    [Fact]
    public async Task Consume_reconnect_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3000));
        await using var server = NatsServer.StartJS();

        var (nats, proxy) = server.CreateProxiedClientConnection();
        await using var nats2 = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var js2 = new NatsJSContext(nats2);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var consumerOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
        };

        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);

        // Not interested in management messages sent upto this point
        await proxy.FlushFramesAsync(nats);

        var cc = await consumer.ConsumeAsync<TestData>(consumerOpts, cancellationToken: cts.Token);

        var readerTask = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var msg in cc.Msgs.ReadAllAsync(cts.Token))
            {
                await msg.AckAsync(new AckOpts(WaitUntilSent: true), cts.Token);
                Assert.Equal(count, msg.Data!.Test);
                count++;

                // We only need two test messages; before and after reconnect.
                if (count == 2)
                    break;
            }
        });

        // Send a message before reconnect
        {
            var ack = await js2.PublishAsync("s1.foo", new TestData { Test = 0 }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        await Retry.Until(
            "acked",
            () => proxy.ClientFrames.Any(f => f.Message.StartsWith("PUB $JS.ACK.s1.c1")));

        Assert.Contains(proxy.ClientFrames, f => f.Message.Contains("CONSUMER.MSG.NEXT"));

        // Simulate server disconnect
        var disconnected = nats.ConnectionDisconnectedAsAwaitable();
        proxy.Reset();
        await disconnected;

        // Make sure reconnected
        await nats.PingAsync(cts.Token);

        // Send a message to be received after reconnect
        {
            var ack = await js2.PublishAsync("s1.foo", new TestData { Test = 1 }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        await Retry.Until(
            "acked",
            () => proxy.ClientFrames.Any(f => f.Message.Contains("CONSUMER.MSG.NEXT")));

        await readerTask;
        await nats.DisposeAsync();
    }

    private record TestData
    {
        public int Test { get; init; }
    }
}
