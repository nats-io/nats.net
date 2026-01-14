using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;

namespace NATS.Client.KeyValueStore.Tests;

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
    public async Task KV_watch_slow_consumer_should_not_block_connection()
    {
        // This test verifies that when a KV watcher is slow (not reading entries),
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
        var kv = new NatsKVContext(js);

        // Separate CTS for stopping the watcher (not for test timeout)
        var watcherCts = new CancellationTokenSource();

        var droppedCount = 0;
        nats.MessageDropped += (_, _) =>
        {
            Interlocked.Increment(ref droppedCount);
            return default;
        };

        var store = await kv.CreateStoreAsync($"{prefix}bucket");

        // Pre-populate with keys (more than channel capacity of 1000)
        const int keyCount = 2000;
        for (var i = 0; i < keyCount; i++)
        {
            await store.PutAsync($"key{i}", BitConverter.GetBytes(i));
        }

        _output.WriteLine($"Created {keyCount} keys");

        // Start a watcher that reads one entry then stops
        var watchStarted = new TaskCompletionSource();
        var watchTask = Task.Run(
            async () =>
            {
                await foreach (var entry in store.WatchAsync<byte[]>("*", cancellationToken: watcherCts.Token))
                {
                    // Consume one entry to prove we're receiving, then stop consuming
                    watchStarted.TrySetResult();

                    // Wait until cancelled - this will cause the internal channel to fill up
                    await Task.Delay(Timeout.Infinite, watcherCts.Token);
                }
            },
            watcherCts.Token);

        await watchStarted.Task;
        await Task.Delay(500); // Give time for channel to fill

        // Run sequential pings - these should NOT be blocked by the slow watcher
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

        watcherCts.Cancel();

        try
        {
            await watchTask;
        }
        catch (OperationCanceledException)
        {
        }

        // Assertions:
        // 1. All pings should succeed (connection not blocked)
        Assert.Equal(20, pingCount);
        Assert.Equal(0, pingErrors);

        // 2. Ping RTT should be reasonable (not blocked for seconds)
        Assert.True(maxPingRttMs < 1000, $"Ping RTT too high ({maxPingRttMs}ms), socket reader may be blocked");

        // 3. Pub/sub messages should flow (socket reader not blocked)
        Assert.Equal(10, pubSubReceived);

        // Note: Message drops may or may not occur depending on timing.
        // The key assertion is that the connection isn't blocked (pings work, pub/sub works).
    }
}
