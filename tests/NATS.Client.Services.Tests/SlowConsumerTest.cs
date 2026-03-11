using NATS.Client.Core.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Services.Tests;

public class SlowConsumerTest
{
    private readonly ITestOutputHelper _output;

    public SlowConsumerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Service_slow_handler_should_not_block_connection()
    {
        // This test verifies that when a service endpoint handler is slow,
        // the connection should NOT be blocked. Pings and pub/sub should continue to work.
        using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = testCts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            SubPendingChannelCapacity = 10,
        });
        await nats.ConnectAsync();

        var svc = new NatsSvcContext(nats);

        var consumerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var droppedCount = 0;
        nats.MessageDropped += (_, _) =>
        {
            Interlocked.Increment(ref droppedCount);
            return default;
        };

        // Create a service with a handler that blocks after first message
        var firstMessageReceived = new TaskCompletionSource();
        var handlerCount = 0;

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);

        await s1.AddEndpointAsync<int>(
            name: "slow-endpoint",
            handler: async m =>
            {
                var count = Interlocked.Increment(ref handlerCount);
                _output.WriteLine($"Handler: message {count}, data={m.Data}");

                if (count == 1)
                {
                    // Signal that we received the first message
                    firstMessageReceived.TrySetResult();
                }

                // Block on all messages - simulating slow handler
                // This will cause the endpoint channel to fill up
                _output.WriteLine($"Handler: blocking on message {count}");
                await Task.Delay(Timeout.Infinite, consumerCts.Token);
            },
            cancellationToken: cancellationToken);

        _output.WriteLine("Service endpoint created");

        // Send many requests to fill the channel (128 capacity + some buffer)
        // These requests don't expect a reply since handler blocks
        var sendTask = Task.Run(
            async () =>
            {
                for (var i = 0; i < 200; i++)
                {
                    await nats.PublishAsync("slow-endpoint", i, cancellationToken: consumerCts.Token);
                }

                _output.WriteLine("Sent 200 messages to slow endpoint");
            },
            consumerCts.Token);

        // Wait for first message to be handled
        var startTimeout = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        var startResult = await Task.WhenAny(firstMessageReceived.Task, startTimeout);

        if (startResult == startTimeout)
        {
            _output.WriteLine("First message did not arrive in time");
            Assert.Fail("First message did not arrive in time");
        }

        _output.WriteLine("First message received, handler is now blocking");
        await Task.Delay(500, cancellationToken); // Give time for channel to fill up

        // Wait for send task to complete (or timeout)
        var sendTimeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        await Task.WhenAny(sendTask, sendTimeout);
        _output.WriteLine($"Send task completed or timed out");

        // Run sequential pings - these should NOT be blocked
        var pingCount = 0;
        var pingErrors = 0;
        var maxPingRttMs = 0.0;

        for (var i = 0; i < 20; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var pingTask = nats.PingAsync().AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
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

            await Task.Delay(100, cancellationToken);
        }

        _output.WriteLine($"Pings succeeded: {pingCount}, failed: {pingErrors}, max RTT: {maxPingRttMs}ms");
        _output.WriteLine($"Messages dropped: {droppedCount}");

        // Test pub/sub to verify regular messages flow
        var pubSubReceived = 0;
        var pubSubSubject = "pubsub.test";

        var pubSubTimeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        var pubSubTask = Task.Run(
            async () =>
            {
                await using var sub = await nats.SubscribeCoreAsync<string>(pubSubSubject, cancellationToken: cancellationToken);

                for (var i = 0; i < 10; i++)
                {
                    await nats.PublishAsync(pubSubSubject, $"msg-{i}", cancellationToken: cancellationToken);
                }

                await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
                {
                    Interlocked.Increment(ref pubSubReceived);
                    _output.WriteLine($"Pub/Sub received: {msg.Data}");
                    if (Volatile.Read(ref pubSubReceived) >= 10)
                        break;
                }
            },
            cancellationToken);

        var pubSubCompleted = await Task.WhenAny(pubSubTask, pubSubTimeout);
        if (pubSubCompleted == pubSubTimeout)
        {
            _output.WriteLine($"Pub/Sub timed out after receiving {pubSubReceived} messages - socket reader blocked!");
        }

        _output.WriteLine($"Pub/Sub messages received: {pubSubReceived}/10");

        // Assertions - verify BEFORE cleanup since cleanup may hang on slow handler:
        // 1. All pings should succeed (connection not blocked)
        Assert.Equal(20, pingCount);
        Assert.Equal(0, pingErrors);

        // 2. Ping RTT should be reasonable (not blocked for seconds)
        Assert.True(maxPingRttMs < 1000, $"Ping RTT too high ({maxPingRttMs}ms), socket reader may be blocked");

        // 3. Pub/sub messages should flow (socket reader not blocked)
        Assert.Equal(10, pubSubReceived);

        // Cleanup - cancel the slow handler, don't wait for it
        consumerCts.Cancel();
    }
}
