using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ConsumerConsumeTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public ConsumerConsumeTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Theory]
    [InlineData("Invalid.DotName")]
    [InlineData("Invalid SpaceName")]
    [InlineData(null)]
    public async Task Consumer_stream_invalid_name_test(string? streamName)
    {
        var jsmContext = new NatsJSContext(new NatsConnection());

        var consumerConfig = new ConsumerConfig("aconsumer");

        // Create ordered consumer
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.CreateOrderedConsumerAsync(streamName!, cancellationToken: CancellationToken.None));

        // Create or update consumer
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.CreateOrUpdateConsumerAsync(streamName!, consumerConfig));

        // Get consumer
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.GetConsumerAsync(streamName!, "aconsumer"));

        // List consumers
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
        {
            await foreach (var unused in jsmContext.ListConsumersAsync(streamName!, CancellationToken.None))
            {
            }
        });

        // List consumer names
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
        {
            await foreach (var unused in jsmContext.ListConsumerNamesAsync(streamName!, CancellationToken.None))
            {
            }
        });

        // Delete consumer
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.DeleteConsumerAsync(streamName!, "aconsumer"));
    }

    [Fact]
    public async Task Consume_msgs_test()
    {
        await using var server = await NatsServer.StartJSAsync();
        var (nats, proxy) = server.CreateProxiedClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        for (var i = 0; i < 30; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumerOpts = new NatsJSConsumeOpts { MaxMsgs = 10 };
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync(serializer: TestDataJsonSerializer<TestData>.Default, consumerOpts, cancellationToken: cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
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
            condition: () => PullCount() > 4,
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
        await using var server = await NatsServer.StartJSWithTraceAsync(_output);

        var (nats, proxy) = server.CreateProxiedClientConnection();

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync("s1.foo", new TestData { Test = 0 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
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
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        var cc = await consumer.ConsumeInternalAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, consumerOpts, cancellationToken: cts.Token);
        await foreach (var msg in cc.Msgs.ReadAllAsync(cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            await signal;
            break;
        }

        await Retry.Until(
            "all pull requests are received",
            () => proxy.ClientFrames.Count(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1")) >= 2);

        var msgNextRequests = proxy
            .ClientFrames
            .Where(f => f.Message.StartsWith("PUB $JS.API.CONSUMER.MSG.NEXT.s1.c1"))
            .ToList();

        // In some cases we are receiving more than two requests which
        // is possible if the tests are running in a slow container and taking
        // more than the timeout? Looking at the test and the code I can't make
        // sense of it, really, but I'm going to assume it's fine to receive 3 pull
        // requests as well as 2 since test failure reported 3 and failed once.
        if (msgNextRequests.Count > 2)
        {
            _output.WriteLine($"Pull request count more than expected: {msgNextRequests.Count}");
            foreach (var frame in msgNextRequests)
            {
                _output.WriteLine($"PULL REQUEST: {frame}");
            }
        }

        // Still fail and check traces if it happens again
        Assert.True(msgNextRequests.Count is 2);

        // Pull requests
        foreach (var frame in msgNextRequests)
        {
            Assert.Matches(@"^PUB.*""batch"":10\b", frame.Message);
        }
    }

    [Fact]
    public async Task Consume_reconnect_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = await NatsServer.StartJSAsync();

        var (nats, proxy) = server.CreateProxiedClientConnection();
        await using var nats2 = await server.CreateClientConnectionAsync();

        var js = new NatsJSContext(nats);
        var js2 = new NatsJSContext(nats2);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var consumerOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
        };

        var consumer = (NatsJSConsumer)await js.GetConsumerAsync("s1", "c1", cts.Token);

        // Not interested in management messages sent upto this point
        await proxy.FlushFramesAsync(nats);

        var readerTask = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, consumerOpts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                Assert.Equal(count, msg.Data!.Test);
                count++;

                // We only need two test messages; before and after reconnect.
                if (count == 2)
                    break;
            }

            return count;
        });

        // Send a message before reconnect
        {
            var ack = await js2.PublishAsync("s1.foo", new TestData { Test = 0 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
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
            var ack = await js2.PublishAsync("s1.foo", new TestData { Test = 1 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var count = await readerTask;
        Assert.Equal(2, count);

        await nats.DisposeAsync();
    }

    [Fact]
    public async Task Consume_dispose_test()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var js = new NatsJSContext(nats);
        var stream = await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var consumerOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 100,
            IdleHeartbeat = TimeSpan.FromSeconds(5),
            Expires = TimeSpan.FromSeconds(10),
        };

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var cc = await consumer.ConsumeInternalAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, consumerOpts, cancellationToken: cts.Token);

        var signal1 = new WaitSignal();
        var signal2 = new WaitSignal();
        var reader = Task.Run(async () =>
        {
            await foreach (var msg in cc.Msgs.ReadAllAsync(cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                signal1.Pulse();
                await signal2;

                // dispose will end the loop
            }
        });

        await signal1;

        // Dispose waits for all the pending messages to be delivered to the loop
        // since the channel reader carries on reading the messages in its internal queue.
        await cc.DisposeAsync();

        // At this point we should only have ACKed one message
        await Retry.Until(
            "ack pending 9",
            async () =>
            {
                var c = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
                return c.Info.NumAckPending == 9;
            },
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(30));
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(9, consumer.Info.NumAckPending);

        signal2.Pulse();

        await reader;

        await Retry.Until(
            "ack pending 0",
            async () =>
            {
                var c = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
                return c.Info.NumAckPending == 0;
            },
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(30));
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    [Fact]
    public async Task Consume_stop_test()
    {
        await using var server = await NatsServer.StartJSAsync();
        await using var nats = await server.CreateClientConnectionAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        var stream = await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var consumerOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 100,
            IdleHeartbeat = TimeSpan.FromSeconds(2),
            Expires = TimeSpan.FromSeconds(4),
        };

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumeStop = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var cc = await consumer.ConsumeInternalAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, consumerOpts, cancellationToken: consumeStop.Token);

        var signal1 = new WaitSignal();
        var signal2 = new WaitSignal();
        var reader = Task.Run(async () =>
        {
            await foreach (var msg in cc.Msgs.ReadAllAsync(cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                signal1.Pulse();
                await signal2;

                // dispose will end the loop
            }
        });

        await signal1;

        // After cancelled consume waits for all the pending messages to be delivered to the loop
        // since the channel reader carries on reading the messages in its internal queue.
        consumeStop.Cancel();

        // At this point we should only have ACKed one message
        await Retry.Until(
            "ack pending 9",
            async () =>
            {
                var c = await js.GetConsumerAsync("s1", "c1", cts.Token);
                return c.Info.NumAckPending == 9;
            },
            timeout: TimeSpan.FromSeconds(20));
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(9, consumer.Info.NumAckPending);

        signal2.Pulse();

        await reader;

        await Retry.Until(
            "ack pending 0",
            async () =>
            {
                var c = await js.GetConsumerAsync("s1", "c1", cts.Token);
                return c.Info.NumAckPending == 0;
            },
            timeout: TimeSpan.FromSeconds(20));
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    [Fact]
    public async Task Serialization_errors()
    {
        await using var server = await NatsServer.StartJSAsync();
        await using var nats = await server.CreateClientConnectionAsync();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        var ack = await js.PublishAsync("s1.foo", "not an int", cancellationToken: cts.Token);
        ack.EnsureSuccess();

        var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            Assert.NotNull(msg.Error);
            Assert.IsType<NatsDeserializeException>(msg.Error);
            Assert.Equal("Exception during deserialization", msg.Error.Message);
            Assert.Contains("Can't deserialize System.Int32", msg.Error.InnerException!.Message);
            Assert.Throws<NatsDeserializeException>(() => msg.EnsureSuccess());

            break;
        }
    }

    [Fact]
    public async Task Consume_right_amount_of_messages()
    {
        await using var server = await NatsServer.StartJSAsync();
        await using var nats = await server.CreateClientConnectionAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", ["s1.*"], cts.Token);

        var payload = new byte[1024];
        for (var i = 0; i < 50; i++)
        {
            var ack = await js.PublishAsync("s1.foo", payload, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        // Max messages
        {
            var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);
            var opts = new NatsJSConsumeOpts { MaxMsgs = 10, };
            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<byte[]>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                if (++count == 4)
                    break;
            }

            await Retry.Until("consumer stats updated", async () =>
            {
                var info = (await js.GetConsumerAsync("s1", "c1", cts.Token)).Info;
                return info is { NumAckPending: 6, NumPending: 40 };
            });
        }

        // Max bytes
        {
            var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c2", cancellationToken: cts.Token);
            var opts = new NatsJSConsumeOpts { MaxBytes = 10 * (1024 + 50), };
            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<byte[]>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                if (++count == 4)
                    break;
            }

            await Retry.Until("consumer stats updated", async () =>
            {
                var info = (await js.GetConsumerAsync("s1", "c2", cts.Token)).Info;
                return info is { NumAckPending: 6, NumPending: 40 };
            });
        }
    }

    [Fact]
    public async Task Consume_right_amount_of_messages_when_ack_wait_exceeded()
    {
        await using var server = await NatsServer.StartJSAsync();
        await using var nats = await server.CreateClientConnectionAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("email-queue", ["email.>"], cts.Token);
        await js.PublishAsync("email.queue", "1", cancellationToken: cts.Token);
        await js.PublishAsync("email.queue", "2", cancellationToken: cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync(
            stream: "email-queue",
            new ConsumerConfig("email-queue-consumer") { AckWait = TimeSpan.FromSeconds(10) },
            cancellationToken: cts.Token);
        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<string>(opts: new NatsJSConsumeOpts { MaxMsgs = 1 }, cancellationToken: cts.Token))
        {
            _output.WriteLine($"Received: {msg.Data}");

            // Only wait for the first couple of messages
            // to get close to the ack wait time
            if (count < 2)
                await Task.Delay(TimeSpan.FromSeconds(6), cts.Token);

            // Since we're pulling one message at a time,
            // we should not exceed the ack wait time
            await msg.AckAsync(cancellationToken: cts.Token);
            count++;
        }

        // Should not have redeliveries
        Assert.Equal(2, count);
    }
}
