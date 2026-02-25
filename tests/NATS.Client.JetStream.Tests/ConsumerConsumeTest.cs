using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using NATS.Net;
using Synadia.Orbit.Testing.NatsServerProcessManager;

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
        var proxy = _server.CreateProxy();
        await using var nats = proxy.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        for (var i = 0; i < 30; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumerOpts = new NatsJSConsumeOpts { MaxMsgs = 10 };
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync(serializer: TestDataJsonSerializer<TestData>.Default, consumerOpts, cancellationToken: cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            count++;
            if (count == 30)
                break;
        }

        await proxy.FlushFramesAsync(nats, false, cts.Token);

        int? PullCount() => proxy?
            .ClientFrames
            .Count(f => f.Message.StartsWith($"PUB $JS.API.CONSUMER.MSG.NEXT.{prefix}s1.{prefix}c1"));

        await Retry.Until(
            reason: "received enough pulls",
            condition: () =>
            {
                var pullCount = PullCount();
                _output.WriteLine($"pullCount: {pullCount}");
                return pullCount > 4;
            },
            retryDelay: TimeSpan.FromSeconds(3),
            timeout: TimeSpan.FromSeconds(60));

        var msgNextRequests = proxy
            .ClientFrames
            .Where(f => f.Message.StartsWith($"PUB $JS.API.CONSUMER.MSG.NEXT.{prefix}s1.{prefix}c1"))
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
        var signal = new WaitSignal(TimeSpan.FromSeconds(60));
        var logger = new InMemoryTestLoggerFactory(LogLevel.Debug, log =>
        {
            if (log is { Category: "NATS.Client.JetStream.Internal.NatsJSConsume", LogLevel: LogLevel.Debug })
            {
                if (log.EventId == NatsJSLogEvents.IdleTimeout)
                    signal.Pulse();
            }
        });

        var proxy = _server.CreateProxy();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{proxy.Port}",
            ConnectTimeout = TimeSpan.FromSeconds(10),
            LoggerFactory = logger,
        });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = 0 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
        ack.EnsureSuccess();

        var consumerOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
            IdleHeartbeat = TimeSpan.FromSeconds(5),
        };
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
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
            () => proxy.ClientFrames.Count(f => f.Message.StartsWith($"PUB $JS.API.CONSUMER.MSG.NEXT.{prefix}s1.{prefix}c1")) >= 2);

        var msgNextRequests = proxy
            .ClientFrames
            .Where(f => f.Message.StartsWith($"PUB $JS.API.CONSUMER.MSG.NEXT.{prefix}s1.{prefix}c1"))
            .ToList();

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
        var proxy = _server.CreateProxy();
        await using var nats = proxy.CreateNatsConnection();
        await nats.ConnectRetryAsync();

        await using var nats2 = _server.CreateNatsConnection();
        await nats2.ConnectRetryAsync();

        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        var js2 = new NatsJSContext(nats2);
        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var consumerOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
        };

        var consumer = (NatsJSConsumer)await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);

        // Not interested in management messages sent upto this point
        await proxy.FlushFramesAsync(nats, clear: true, cts.Token);

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
            var ack = await js2.PublishAsync($"{prefix}s1.foo", new TestData { Test = 0 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        await Retry.Until(
            "acked",
            () => proxy.ClientFrames.Any(f => f.Message.StartsWith($"PUB $JS.ACK.{prefix}s1.{prefix}c1")),
            timeout: TimeSpan.FromSeconds(20),
            retryDelay: TimeSpan.FromSeconds(1));

        Assert.Contains(proxy.ClientFrames, f => f.Message.Contains("CONSUMER.MSG.NEXT"));

        // Simulate server disconnect
        var disconnected = nats.ConnectionDisconnectedAsAwaitable();
        proxy.Reset();
        await disconnected;

        // Make sure reconnected
        await nats.PingAsync(cts.Token);

        // Send a message to be received after reconnect
        {
            var ack = await js2.PublishAsync($"{prefix}s1.foo", new TestData { Test = 1 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
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
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
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
        var reader = Task.Run(
            async () =>
            {
                await foreach (var msg in cc.Msgs.ReadAllAsync(cts.Token))
                {
                    await msg.AckAsync(cancellationToken: cts.Token);
                    signal1.Pulse();
                    await signal2;

                    // dispose will end the loop
                }
            },
            cts.Token);

        await signal1;

        // Wait until all 10 messages have been delivered to the consumer
        // (NumAckPending == 9 means 10 delivered, 1 acked by reader, 9 pending)
        await Retry.Until(
            "all messages delivered",
            async () =>
            {
                var c = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
                return c.Info.NumAckPending == 9;
            },
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(30));

        // Now dispose - all messages are safely in the channel.
        // Dispose waits for all the pending messages to be delivered to the loop
        // since the channel reader carries on reading the messages in its internal queue.
        await cc.DisposeAsync();

        // At this point we should only have ACKed one message
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
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        var stream = await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var consumerOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 100,
            IdleHeartbeat = TimeSpan.FromSeconds(2),
            Expires = TimeSpan.FromSeconds(4),
        };

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
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
                var c = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
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
                var c = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
                return c.Info.NumAckPending == 0;
            },
            timeout: TimeSpan.FromSeconds(20));
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    [Fact]
    public async Task Serialization_errors()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();

        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);

        var ack = await js.PublishAsync($"{prefix}s1.foo", "not an int", cancellationToken: cts.Token);
        ack.EnsureSuccess();

        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

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
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var payload = new byte[1024];
        for (var i = 0; i < 50; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", payload, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        // Max messages
        {
            var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);
            var opts = new NatsJSConsumeOpts { MaxMsgs = 10, };
            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<byte[]>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                if (++count == 4)
                    break;
            }

            await Retry.Until(
                "consumer stats updated for Max messages",
                async () =>
                {
                    var info = (await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token)).Info;
                    _output.WriteLine($"Consumer stats updated for Max messages {info.NumAckPending} of {info.NumPending}");
                    return info is { NumAckPending: 6, NumPending: 40 };
                },
                retryDelay: TimeSpan.FromSeconds(3),
                timeout: TimeSpan.FromSeconds(60));
        }

        // Max bytes
        {
            var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c2", cancellationToken: cts.Token);
            var opts = new NatsJSConsumeOpts { MaxBytes = 10 * (1024 + 50), };
            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<byte[]>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                if (++count == 4)
                    break;
            }

            await Retry.Until(
                "consumer stats updated for Max bytes",
                async () =>
                {
                    var info = (await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c2", cts.Token)).Info;
                    _output.WriteLine($"Consumer stats updated for Max bytes {info.NumAckPending} of {info.NumPending}");
                    return info is { NumAckPending: 5, NumPending: 41 };
                },
                retryDelay: TimeSpan.FromSeconds(3),
                timeout: TimeSpan.FromSeconds(60));
        }
    }

    [Fact]
    public async Task Consume_right_amount_of_messages_when_ack_wait_exceeded()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}email-queue", [$"{prefix}email.>"], cts.Token);
        await js.PublishAsync($"{prefix}email.queue", "1", cancellationToken: cts.Token);
        await js.PublishAsync($"{prefix}email.queue", "2", cancellationToken: cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync(
            stream: $"{prefix}email-queue",
            new ConsumerConfig($"{prefix}email-queue-consumer") { AckWait = TimeSpan.FromSeconds(10) },
            cancellationToken: cts.Token);
        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<string>(opts: new NatsJSConsumeOpts { MaxMsgs = 1 }, cancellationToken: cts.Token))
        {
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

    [Fact]
    public async Task Consume_pending_reset_on_reconnect_when_using_ephemeral_consumer_503()
    {
        var logger = new InMemoryTestLoggerFactory(LogLevel.Debug);
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logger });
        await nats.ConnectRetryAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var js = nats.CreateJetStreamContext();
        await js.CreateStreamAsync(new StreamConfig("s1", ["s1.*"]), cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig("c1") { MemStorage = true }, cts.Token);

        _ = Task.Run(
            async () =>
            {
                await foreach (var msg in consumer.ConsumeAsync<string>(cancellationToken: cts.Token))
                {
                }
            },
            cts.Token);

        await Retry.Until(
            "first pull request received",
            () => logger.Logs.Any(m => m.EventId == NatsJSLogEvents.PullRequest));

        await server.RestartAsync();

        await Retry.Until(
            reason: "more pull request received",
            condition: () => logger.Logs.Count(m => m.EventId == NatsJSLogEvents.PullRequest) > 1,
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(60));

        // After reconnect, we should not flood with pull requests
        // since the consumer should reset (zero-out) its pending messages.
        // This is to prevent flooding the server with pull requests
        // when the consumer is not receiving messages.
        // Wait for a few seconds to ensure more pull requests are sent.
        await Task.Delay(3000, cts.Token);

        var pullRequestCount = logger.Logs.Count(m => m.EventId == NatsJSLogEvents.PullRequest);
        pullRequestCount.Should().BeLessThanOrEqualTo(4, "should not flood with pull requests after reconnect");
    }

    [Fact]
    public async Task Consume_connection_failed_test()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            MaxReconnectRetry = 2,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.Zero,
        });
        await nats.ConnectAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", ["s1.*"], cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var started = new TaskCompletionSource();

        // Start consuming in background
        var consumeTask = Task.Run(async () =>
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 10,
                IdleHeartbeat = TimeSpan.FromSeconds(1),
                Expires = TimeSpan.FromSeconds(2),
            };
            started.SetResult();
            await foreach (var msg in consumer.ConsumeAsync<string>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
            }
        });

        // Wait for consume to start
        await started.Task;
        await Task.Delay(500);

        // Stop server to trigger connection failure
        await server.StopAsync();

        // Wait for reconnect failure
        var exception = await Assert.ThrowsAsync<NatsConnectionFailedException>(async () => await consumeTask);

        // Message could be either from connection or from consume internal checks
        Assert.True(
            exception.Message.Contains("Connection is in failed state") ||
            exception.Message.Contains("Maximum connection retry attempts exceeded"),
            $"Unexpected exception message: {exception.Message}");

        // Verify connection state is Failed
        Assert.Equal(NatsConnectionState.Failed, nats.ConnectionState);
    }

    [Fact]
    public async Task Consume_503_threshold_configuration_test()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        // Publish some test messages
        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        // Test with custom threshold value
        {
            var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);
            var messagesReceived = 0;
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 10,
                MaxConsecutive503Errors = 5, // Custom threshold
            };
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                messagesReceived++;
                if (messagesReceived == 3)
                    break;
            }

            Assert.Equal(3, messagesReceived);
        }

        // Test with disabled threshold
        {
            var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c2", cancellationToken: cts.Token);
            var messagesReceived = 0;
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 10,
                MaxConsecutive503Errors = -1, // Disabled
            };
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                messagesReceived++;
                if (messagesReceived == 3)
                    break;
            }

            Assert.Equal(3, messagesReceived);
        }

        // Test with default threshold (10)
        {
            var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c3", cancellationToken: cts.Token);
            var messagesReceived = 0;
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 10,

                // MaxConsecutive503Errors defaults to 10
            };
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                messagesReceived++;
                if (messagesReceived == 3)
                    break;
            }

            Assert.Equal(3, messagesReceived);
        }
    }

    [Fact]
    public async Task Consume_503_counter_resets_on_success_test()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", ["s1.*"], cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var messagesReceived = 0;
        var opts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
            MaxConsecutive503Errors = 5,
        };

        // Start consuming
        var consumeTask = Task.Run(async () =>
        {
            await foreach (var msg in consumer.ConsumeAsync<string>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                messagesReceived++;
                if (messagesReceived == 2)
                    break;
            }
        });

        // Publish messages to reset the counter
        await js.PublishAsync("s1.foo", "message1", cancellationToken: cts.Token);
        await Task.Delay(500); // Wait for message to be consumed

        // At this point, 503 counter should be reset to 0 after successful message
        await js.PublishAsync("s1.foo", "message2", cancellationToken: cts.Token);

        await consumeTask;

        Assert.Equal(2, messagesReceived);
    }

    [Fact]
    public async Task Consume_ephemeral_consumer_deleted_on_server_terminates_with_409()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var js = nats.CreateJetStreamContext();
        await js.CreateStreamAsync(new StreamConfig("s1", ["s1.*"]), cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig("c1") { MemStorage = true }, cts.Token);

        // Publish messages
        for (var i = 0; i < 5; i++)
        {
            var ack = await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var firstMessageConsumed = new WaitSignal(TimeSpan.FromSeconds(30));
        var messagesReceived = 0;

        var consumeTask = Task.Run(async () =>
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 10,
                MaxConsecutive503Errors = 3,
            };

            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                Interlocked.Increment(ref messagesReceived);
                firstMessageConsumed.Pulse();
            }
        });

        // Wait until at least one message is consumed, then delete the consumer
        await firstMessageConsumed;
        Assert.True(messagesReceived > 0, "Should have received at least one message before deletion");

        await js.DeleteConsumerAsync("s1", "c1", cts.Token);

        // Server sends 409 Consumer Deleted to the active pull, which is a terminal error
        var exception = await Assert.ThrowsAnyAsync<NatsJSException>(async () => await consumeTask);
        Assert.IsType<NatsJSProtocolException>(exception);
        var protocolException = (NatsJSProtocolException)exception;
        Assert.Equal(409, protocolException.HeaderCode);
        Assert.Contains("Consumer Deleted", protocolException.Message);
    }

    [Fact]
    public async Task Consume_ephemeral_memstorage_consumer_server_restart_terminates_with_503()
    {
        var logger = new InMemoryTestLoggerFactory(LogLevel.Debug);
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logger });
        await nats.ConnectRetryAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var js = nats.CreateJetStreamContext();
        await js.CreateStreamAsync(new StreamConfig("s1", ["s1.*"]), cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig("c1") { MemStorage = true }, cts.Token);

        // Publish messages
        for (var i = 0; i < 5; i++)
        {
            var ack = await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var firstMessageConsumed = new WaitSignal(TimeSpan.FromSeconds(30));

        var consumeTask = Task.Run(async () =>
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 10,
                MaxConsecutive503Errors = 3,
                IdleHeartbeat = TimeSpan.FromSeconds(1),
                Expires = TimeSpan.FromSeconds(2),
            };

            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                firstMessageConsumed.Pulse();
            }
        });

        // Wait until at least one message is consumed, then restart server
        await firstMessageConsumed;

        await server.RestartAsync();

        // MemStorage consumer vanishes on restart. The server doesn't send 409 because
        // it doesn't know about the old consumer, so pulls return 503 no responders.
        var exception = await Assert.ThrowsAnyAsync<NatsJSException>(async () => await consumeTask);
        Assert.Contains("503", exception.Message);

        // Verify NoResponders log events were accumulated
        var noResponderLogs = logger.Logs.Count(m => m.EventId == NatsJSLogEvents.NoResponders);
        Assert.True(noResponderLogs > 0, "Should have logged NoResponders events");
    }

    [Fact]
    public async Task Consume_slow_message_processing_does_not_prevent_consumer_deleted_detection()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var js = nats.CreateJetStreamContext();
        await js.CreateStreamAsync(new StreamConfig("s1", ["s1.*"]), cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig("c1") { MemStorage = true }, cts.Token);

        // Publish messages
        for (var i = 0; i < 5; i++)
        {
            var ack = await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var firstMessageReceived = new WaitSignal(TimeSpan.FromSeconds(30));
        var messagesProcessed = 0;

        var consumeTask = Task.Run(async () =>
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 10,
                MaxConsecutive503Errors = 3,
            };

            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                firstMessageReceived.Pulse();

                // Simulate slow processing - consumer will be deleted during this delay
                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

                await msg.AckAsync(cancellationToken: cts.Token);
                Interlocked.Increment(ref messagesProcessed);
            }
        });

        // Wait for first message to arrive, then delete consumer during slow processing
        await firstMessageReceived;

        await js.DeleteConsumerAsync("s1", "c1", cts.Token);

        // ConsumeAsync should still terminate despite slow message processing.
        // The server sends 409 Consumer Deleted which terminates the channel writer.
        var exception = await Assert.ThrowsAnyAsync<NatsJSException>(async () => await consumeTask);
        Assert.IsType<NatsJSProtocolException>(exception);
        var protocolException = (NatsJSProtocolException)exception;
        Assert.Equal(409, protocolException.HeaderCode);
        Assert.Contains("Consumer Deleted", protocolException.Message);
    }

    [Fact]
    public async Task Consume_buffered_messages_with_slow_processing_still_detects_503_after_server_restart()
    {
        // When messages are buffered and being slowly processed, the heartbeat timer
        // is the only mechanism to drive new pull requests after 503 (since ResetPending
        // sets pending to max, preventing CheckPending from issuing pulls).
        // We verify that even with slow message processing, the 503 threshold is
        // eventually hit via timer-driven pulls after the consumer vanishes.
        var logger = new InMemoryTestLoggerFactory(LogLevel.Debug);
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logger });
        await nats.ConnectRetryAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var js = nats.CreateJetStreamContext();
        await js.CreateStreamAsync(new StreamConfig("s1", ["s1.*"]), cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig("c1") { MemStorage = true }, cts.Token);

        // Publish many messages so the buffer has data to process slowly
        for (var i = 0; i < 20; i++)
        {
            var ack = await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var firstMessageConsumed = new WaitSignal(TimeSpan.FromSeconds(30));
        var messagesProcessed = 0;

        var consumeTask = Task.Run(async () =>
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 100,
                MaxConsecutive503Errors = 3,
                IdleHeartbeat = TimeSpan.FromSeconds(1),
                Expires = TimeSpan.FromSeconds(2),
            };

            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                var count = Interlocked.Increment(ref messagesProcessed);
                firstMessageConsumed.Pulse();

                // Slow processing on all messages
                await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
            }
        });

        // Wait until messages start flowing, then restart server
        await firstMessageConsumed;

        await server.RestartAsync();

        // Even though the consumer is slowly processing buffered messages,
        // the 503 detection should still terminate ConsumeAsync.
        // After restart, the MemStorage consumer is gone, so timer-driven pulls
        // return 503 and accumulate until the threshold is hit.
        var exception = await Assert.ThrowsAnyAsync<NatsJSException>(async () => await consumeTask);
        Assert.Contains("503", exception.Message);

        // Verify some messages were processed before termination
        Assert.True(messagesProcessed > 0, "Should have processed at least one message");

        // Verify 503 errors accumulated
        var noResponderLogs = logger.Logs.Count(m => m.EventId == NatsJSLogEvents.NoResponders);
        Assert.True(noResponderLogs > 0, "Should have logged NoResponders events");
    }

    [Fact]
    public async Task Consume_503_counter_accumulates_across_heartbeat_timer_pulls()
    {
        var logger = new InMemoryTestLoggerFactory(LogLevel.Debug);
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logger });
        await nats.ConnectRetryAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var js = nats.CreateJetStreamContext();
        await js.CreateStreamAsync(new StreamConfig("s1", ["s1.*"]), cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig("c1") { MemStorage = true }, cts.Token);

        // Publish a message so consume loop starts
        var ack = await js.PublishAsync("s1.foo", 1, cancellationToken: cts.Token);
        ack.EnsureSuccess();

        var firstMessageConsumed = new WaitSignal(TimeSpan.FromSeconds(30));

        var consumeTask = Task.Run(async () =>
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 10,
                MaxConsecutive503Errors = 5,
                IdleHeartbeat = TimeSpan.FromSeconds(1),
                Expires = TimeSpan.FromSeconds(2),
            };

            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                firstMessageConsumed.Pulse();
            }
        });

        // Wait for message to be consumed, then restart server so consumer vanishes
        // without the server sending a 409 notification
        await firstMessageConsumed;

        await server.RestartAsync();

        // Heartbeat timer fires repeatedly, generating pull requests that return 503
        // because the MemStorage consumer no longer exists after restart
        var exception = await Assert.ThrowsAnyAsync<NatsJSException>(async () => await consumeTask);
        Assert.Contains("503", exception.Message);

        // Verify that idle timeout events were logged (heartbeat timer callbacks)
        await Retry.Until(
            "idle timeout logs present",
            () => logger.Logs.Count(m => m.EventId == NatsJSLogEvents.IdleTimeout) > 0,
            retryDelay: TimeSpan.FromMilliseconds(500),
            timeout: TimeSpan.FromSeconds(10));

        // Verify 503 counter accumulated from timer-originated pulls
        var noResponderLogs = logger.Logs.Count(m => m.EventId == NatsJSLogEvents.NoResponders);
        Assert.True(noResponderLogs >= 5, $"Should have accumulated at least 5 NoResponders events, got {noResponderLogs}");
    }
}
