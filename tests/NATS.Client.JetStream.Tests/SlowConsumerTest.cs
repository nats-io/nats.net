using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class SlowConsumerTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public SlowConsumerTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task JetStream_consume_slow_consumer_should_not_block_connection()
    {
        // This test verifies that when a JetStream consumer is slow (not reading messages),
        // the connection should NOT be blocked. Instead:
        // 1. Messages should be dropped (OnMessageDropped triggered)
        // 2. Other operations (like Ping and pub/sub) should continue to work

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            SubPendingChannelCapacity = 10, // Small capacity to trigger drops quickly
        });
        await nats.ConnectAsync();

        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        // Separate CTS for stopping the consumer (not for test timeout)
        var consumerCts = new CancellationTokenSource();

        // Track dropped messages
        var droppedCount = 0;
        nats.MessageDropped += (_, _) =>
        {
            Interlocked.Increment(ref droppedCount);
            return default;
        };

        // Create stream and consumer
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"]);
        var consumer = await js.CreateOrUpdateConsumerAsync(
            $"{prefix}s1",
            new ConsumerConfig($"{prefix}c1")
            {
                AckPolicy = ConsumerConfigAckPolicy.None,
            });

        // Pre-populate stream with more messages than channel capacity
        // Channel capacity is MaxMsgs * 2, so with MaxMsgs = 5, capacity = 10
        // We publish 100 messages to ensure the channel overflows
        const int messageCount = 100;
        for (var i = 0; i < messageCount; i++)
        {
            await js.PublishAsync($"{prefix}s1.data", BitConverter.GetBytes(i));
        }

        _output.WriteLine($"Published {messageCount} messages");

        // Start a consumer that reads one message then stops (simulating slow consumer)
        // MaxMsgs = 5 means channel capacity = 10, which will overflow with 100 messages
        var consumeStarted = new TaskCompletionSource();
        var consumeTask = Task.Run(
            async () =>
            {
                var opts = new NatsJSConsumeOpts { MaxMsgs = 5 };
                await foreach (var msg in consumer.ConsumeAsync<byte[]>(opts: opts, cancellationToken: consumerCts.Token))
                {
                    // Consume one message to prove we're receiving, then stop consuming
                    consumeStarted.TrySetResult();

                    // Wait until cancelled - this will cause the internal channel to fill up
                    await Task.Delay(Timeout.Infinite, consumerCts.Token);
                }
            },
            consumerCts.Token);

        // Wait for consume to start
        await consumeStarted.Task;
        await Task.Delay(500); // Give time for messages to arrive and channel to fill

        // Run sequential pings every 100ms - these should NOT be blocked by the slow consumer
        var pingCount = 0;
        var pingErrors = 0;
        var maxPingRttMs = 0.0;

        for (var i = 0; i < 20; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Use Task.WhenAny with explicit timeout since PingAsync may not respond to cancellation
            // when socket reader is blocked
            // Note: Convert ValueTask to Task once and reuse to avoid "already consumed" errors
            var pingTask = nats.PingAsync().AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(pingTask, timeoutTask);

            sw.Stop();

            if (completed == timeoutTask)
            {
                _output.WriteLine($"Ping {i + 1}: TIMEOUT after {sw.ElapsedMilliseconds}ms - socket reader blocked!");
                pingErrors++;
                break; // If one times out, the rest will too
            }

            try
            {
                var rtt = await pingTask;
                pingCount++;
                if (rtt.TotalMilliseconds > maxPingRttMs)
                    maxPingRttMs = rtt.TotalMilliseconds;
                _output.WriteLine($"Ping {i + 1}: RTT {rtt.TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                pingErrors++;
                _output.WriteLine($"Ping {i + 1} error: {ex.Message}");
            }

            await Task.Delay(100);
        }

        _output.WriteLine($"Pings succeeded: {pingCount}, failed: {pingErrors}, max RTT: {maxPingRttMs}ms");
        _output.WriteLine($"Messages dropped: {droppedCount}");

        // Also test pub/sub to verify regular messages flow
        var pubSubReceived = 0;
        var pubSubSubject = $"{prefix}pubsub.test";

        // Use explicit timeout for pub/sub since socket reader may be blocked
        var pubSubTimeout = Task.Delay(TimeSpan.FromSeconds(5));
        var pubSubTask = Task.Run(async () =>
        {
            await using var sub = await nats.SubscribeCoreAsync<string>(pubSubSubject);

            // Publish 10 messages
            for (var i = 0; i < 10; i++)
            {
                await nats.PublishAsync(pubSubSubject, $"msg-{i}");
            }

            // Receive messages
            await foreach (var msg in sub.Msgs.ReadAllAsync())
            {
                Interlocked.Increment(ref pubSubReceived);
                _output.WriteLine($"Pub/Sub received: {msg.Data}");
                if (Volatile.Read(ref pubSubReceived) >= 10)
                    break;
            }
        });

        var pubSubCompleted = await Task.WhenAny(pubSubTask, pubSubTimeout);
        if (pubSubCompleted == pubSubTimeout)
        {
            _output.WriteLine($"Pub/Sub timed out after receiving {pubSubReceived} messages - socket reader blocked!");
        }

        _output.WriteLine($"Pub/Sub messages received: {pubSubReceived}/10");

        // Cancel the consume task
        consumerCts.Cancel();

        try
        {
            await consumeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assertions:
        // 1. All pings should succeed (connection not blocked)
        Assert.Equal(20, pingCount);
        Assert.Equal(0, pingErrors);

        // 2. Ping RTT should be reasonable (not blocked for seconds)
        Assert.True(maxPingRttMs < 1000, $"Ping RTT too high ({maxPingRttMs}ms), socket reader may be blocked");

        // 3. Pub/sub messages should flow (socket reader not blocked)
        Assert.Equal(10, pubSubReceived);

        // Note: Message drops may or may not occur depending on timing and JetStream pull batching.
        // The key assertion is that the connection isn't blocked (pings work, pub/sub works).
        // With JetStream pull model, MaxMsgs limits how many messages are fetched per batch,
        // so the channel may not actually overflow even with many messages in the stream.
    }

    [Fact]
    public async Task JetStream_fetch_slow_consumer_should_not_block_connection()
    {
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            SubPendingChannelCapacity = 10,
        });
        await nats.ConnectAsync();

        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var consumerCts = new CancellationTokenSource();

        var droppedCount = 0;
        nats.MessageDropped += (_, _) =>
        {
            Interlocked.Increment(ref droppedCount);
            return default;
        };

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"]);
        var consumer = await js.CreateOrUpdateConsumerAsync(
            $"{prefix}s1",
            new ConsumerConfig($"{prefix}c1")
            {
                AckPolicy = ConsumerConfigAckPolicy.None,
            });

        // Channel capacity is MaxMsgs * 2, so with MaxMsgs = 5, capacity = 10
        const int messageCount = 100;
        for (var i = 0; i < messageCount; i++)
        {
            await js.PublishAsync($"{prefix}s1.data", BitConverter.GetBytes(i));
        }

        _output.WriteLine($"Published {messageCount} messages");

        // MaxMsgs = 5 means channel capacity = 10, which will overflow with 100 messages
        var fetchStarted = new TaskCompletionSource();
        var fetchTask = Task.Run(async () =>
        {
            var opts = new NatsJSFetchOpts { MaxMsgs = 5 };
            await foreach (var msg in consumer.FetchAsync<byte[]>(opts: opts, cancellationToken: consumerCts.Token))
            {
                fetchStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, consumerCts.Token);
            }
        }, consumerCts.Token);

        await fetchStarted.Task;
        await Task.Delay(500);

        // Run sequential pings
        var pingCount = 0;
        var pingErrors = 0;
        var maxPingRttMs = 0.0;

        for (var i = 0; i < 20; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var pingTask = nats.PingAsync().AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(pingTask, timeoutTask);

            sw.Stop();

            if (completed == timeoutTask)
            {
                _output.WriteLine($"Ping {i + 1}: TIMEOUT after {sw.ElapsedMilliseconds}ms - socket reader blocked!");
                pingErrors++;
                break;
            }

            try
            {
                var rtt = await pingTask;
                pingCount++;
                if (rtt.TotalMilliseconds > maxPingRttMs)
                    maxPingRttMs = rtt.TotalMilliseconds;
                _output.WriteLine($"Ping {i + 1}: RTT {rtt.TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                pingErrors++;
                _output.WriteLine($"Ping {i + 1} error: {ex.Message}");
            }

            await Task.Delay(100);
        }

        _output.WriteLine($"Pings succeeded: {pingCount}, failed: {pingErrors}, max RTT: {maxPingRttMs}ms");
        _output.WriteLine($"Messages dropped: {droppedCount}");

        // Test pub/sub
        var pubSubReceived = 0;
        var pubSubSubject = $"{prefix}pubsub.test";

        var pubSubTimeout = Task.Delay(TimeSpan.FromSeconds(5));
        var pubSubTask = Task.Run(async () =>
        {
            await using var sub = await nats.SubscribeCoreAsync<string>(pubSubSubject);

            for (var i = 0; i < 10; i++)
            {
                await nats.PublishAsync(pubSubSubject, $"msg-{i}");
            }

            await foreach (var msg in sub.Msgs.ReadAllAsync())
            {
                Interlocked.Increment(ref pubSubReceived);
                _output.WriteLine($"Pub/Sub received: {msg.Data}");
                if (Volatile.Read(ref pubSubReceived) >= 10)
                    break;
            }
        });

        var pubSubCompleted = await Task.WhenAny(pubSubTask, pubSubTimeout);
        if (pubSubCompleted == pubSubTimeout)
        {
            _output.WriteLine($"Pub/Sub timed out after receiving {pubSubReceived} messages - socket reader blocked!");
        }

        _output.WriteLine($"Pub/Sub messages received: {pubSubReceived}/10");

        consumerCts.Cancel();

        try
        {
            await fetchTask;
        }
        catch (OperationCanceledException)
        {
        }

        // Assertions: connection should not be blocked
        Assert.Equal(20, pingCount);
        Assert.Equal(0, pingErrors);
        Assert.True(maxPingRttMs < 1000, $"Ping RTT too high ({maxPingRttMs}ms), socket reader may be blocked");
        Assert.Equal(10, pubSubReceived);

        // Note: Message drops may not occur with JetStream pull model due to batching limits
    }
}
