using NATS.Client.Core2.Tests;

namespace NATS.Client.Core.Tests;

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
    public async Task Slow_consumer()
    {
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            SubPendingChannelCapacity = 3,
        });

        var lost = 0;
        nats.MessageDropped += (_, e) =>
        {
            if (e is { } dropped)
            {
                Interlocked.Increment(ref lost);
                _output.WriteLine($"LOST {dropped.Data}");
            }

            return default;
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var sync = 0;
        var end = 0;
        var count = 0;
        var signal = new WaitSignal();
        var subscription = Task.Run(
            async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<string>("foo.>", cancellationToken: cancellationToken))
                {
                    if (msg.Subject == "foo.sync")
                    {
                        Interlocked.Increment(ref sync);
                        continue;
                    }

                    if (msg.Subject == "foo.end")
                    {
                        Interlocked.Increment(ref end);
                        break;
                    }

                    await signal;

                    await Task.Delay(100, cancellationToken);

                    Interlocked.Increment(ref count);
                    _output.WriteLine($"GOOD {msg.Data}");
                }
            },
            cancellationToken);

        await Retry.Until(
            "subscription is ready",
            () => Volatile.Read(ref sync) > 0,
            async () => await nats.PublishAsync("foo.sync", cancellationToken: cancellationToken));

        for (var i = 0; i < 10; i++)
        {
            await nats.PublishAsync("foo.data", $"A{i}", cancellationToken: cancellationToken);
        }

        signal.Pulse();

        for (var i = 0; i < 10; i++)
        {
            await nats.PublishAsync("foo.data", $"B{i}", cancellationToken: cancellationToken);
        }

        await Retry.Until(
            "subscription is ended",
            () => Volatile.Read(ref end) > 0,
            async () => await nats.PublishAsync("foo.end", cancellationToken: cancellationToken));

        await subscription;

        // we should've lost most of the messages because of the low channel capacity
        Volatile.Read(ref count).Should().BeLessThan(20);
        Volatile.Read(ref lost).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SlowConsumerDetected_fires_once_per_episode()
    {
        // This test verifies that SlowConsumerDetected event fires only once
        // when a subscription becomes a slow consumer, not on every dropped message.
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            SubPendingChannelCapacity = 3,
        });

        var droppedCount = 0;
        var slowConsumerCount = 0;
        NatsSubBase? slowConsumerSub = null;

        nats.MessageDropped += (_, e) =>
        {
            Interlocked.Increment(ref droppedCount);
            _output.WriteLine($"MessageDropped: {e.Subject}");
            return default;
        };

        nats.SlowConsumerDetected += (_, e) =>
        {
            Interlocked.Increment(ref slowConsumerCount);
            slowConsumerSub = e.Subscription;
            _output.WriteLine($"SlowConsumerDetected: {e.Subscription.Subject}");
            return default;
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var sync = 0;
        var signal = new WaitSignal();

        // Start a slow subscription that blocks after receiving sync message
        var subscription = Task.Run(
            async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<string>("test.>", cancellationToken: cancellationToken))
                {
                    if (msg.Subject == "test.sync")
                    {
                        Interlocked.Increment(ref sync);
                        await signal; // Block here to cause slow consumer
                        continue;
                    }

                    if (msg.Subject == "test.end")
                    {
                        break;
                    }
                }
            },
            cancellationToken);

        // Wait for subscription to be ready
        await Retry.Until(
            "subscription is ready",
            () => Volatile.Read(ref sync) > 0,
            async () => await nats.PublishAsync("test.sync", "sync", cancellationToken: cancellationToken));

        // Publish many messages to trigger multiple drops
        for (var i = 0; i < 20; i++)
        {
            await nats.PublishAsync("test.data", $"msg{i}", cancellationToken: cancellationToken);
        }

        // Wait for messages to be dropped
        await Retry.Until(
            "messages are dropped",
            () => Volatile.Read(ref droppedCount) > 1);

        // Release the subscription
        signal.Pulse();

        await Retry.Until(
            "subscription ended",
            () => true,
            async () => await nats.PublishAsync("test.end", cancellationToken: cancellationToken));

        await subscription;

        _output.WriteLine($"Dropped: {droppedCount}, SlowConsumerDetected: {slowConsumerCount}");

        // Key assertion: SlowConsumerDetected should fire exactly once,
        // even though multiple messages were dropped
        Volatile.Read(ref droppedCount).Should().BeGreaterThan(1, "multiple messages should be dropped");
        Volatile.Read(ref slowConsumerCount).Should().Be(1, "SlowConsumerDetected should fire only once per episode");
        slowConsumerSub.Should().NotBeNull();
        slowConsumerSub!.Subject.Should().Be("test.>");
    }

    [Fact]
    public async Task SlowConsumerDetected_fires_again_after_recovery()
    {
        // This test verifies that SlowConsumerDetected fires again if the SAME subscription
        // recovers (channel drains to near empty) and then becomes slow again.
        // The subscription processes messages slowly (with delay), allowing us to:
        // 1. Overflow the channel (fire slow consumer)
        // 2. Wait for channel to drain (recovery)
        // 3. Overflow again (fire slow consumer again)
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            SubPendingChannelCapacity = 3,
        });

        var droppedCount = 0;
        var slowConsumerCount = 0;

        nats.MessageDropped += (_, e) =>
        {
            Interlocked.Increment(ref droppedCount);
            _output.WriteLine($"MessageDropped: {e.Subject}");
            return default;
        };

        nats.SlowConsumerDetected += (_, e) =>
        {
            var count = Interlocked.Increment(ref slowConsumerCount);
            _output.WriteLine($"SlowConsumerDetected #{count}: {e.Subscription.Subject}");
            return default;
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var sync = 0;
        var processedCount = 0;
        var processingDelay = 50; // ms per message

        // Start a slow subscription that processes messages with delay
        var subscription = Task.Run(
            async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<string>("recovery.>", cancellationToken: cancellationToken))
                {
                    if (msg.Subject == "recovery.sync")
                    {
                        Interlocked.Increment(ref sync);
                        continue;
                    }

                    if (msg.Subject == "recovery.end")
                    {
                        break;
                    }

                    // Process slowly to allow channel to fill up during bursts
                    await Task.Delay(processingDelay, cancellationToken);
                    var count = Interlocked.Increment(ref processedCount);
                    _output.WriteLine($"Processed #{count}: {msg.Data}");
                }
            },
            cancellationToken);

        // Wait for subscription to be ready
        await Retry.Until(
            "subscription is ready",
            () => Volatile.Read(ref sync) > 0,
            async () => await nats.PublishAsync("recovery.sync", cancellationToken: cancellationToken));

        // === Episode 1: Burst of messages to overflow channel ===
        _output.WriteLine("=== Episode 1: Bursting messages ===");
        for (var i = 0; i < 20; i++)
        {
            await nats.PublishAsync("recovery.data", $"episode1-{i}", cancellationToken: cancellationToken);
        }

        // Wait for drops to happen
        await Retry.Until(
            "episode 1 drops",
            () => Volatile.Read(ref droppedCount) > 0);

        var droppedAfterEpisode1 = Volatile.Read(ref droppedCount);
        var slowConsumerAfterEpisode1 = Volatile.Read(ref slowConsumerCount);
        _output.WriteLine($"After episode 1 burst - Dropped: {droppedAfterEpisode1}, SlowConsumer: {slowConsumerAfterEpisode1}");

        // === Recovery: Wait for subscription to drain the channel ===
        _output.WriteLine("=== Recovery: Waiting for channel to drain ===");

        // Wait for enough messages to be processed so the channel drains (capacity is 3)
        // We need at least 3 messages processed for the channel to be empty
        await Retry.Until(
            "channel drained",
            () => Volatile.Read(ref processedCount) >= 3);

        var processedAfterRecovery = Volatile.Read(ref processedCount);
        _output.WriteLine($"After recovery - Processed: {processedAfterRecovery}");

        // === Episode 2: Another burst to overflow channel again ===
        _output.WriteLine("=== Episode 2: Bursting more messages ===");
        for (var i = 0; i < 20; i++)
        {
            await nats.PublishAsync("recovery.data", $"episode2-{i}", cancellationToken: cancellationToken);
        }

        // Wait for more drops and the second slow consumer event
        await Retry.Until(
            "episode 2 drops",
            () => Volatile.Read(ref droppedCount) > droppedAfterEpisode1 && Volatile.Read(ref slowConsumerCount) >= 2);

        var droppedAfterEpisode2 = Volatile.Read(ref droppedCount);
        var slowConsumerAfterEpisode2 = Volatile.Read(ref slowConsumerCount);
        _output.WriteLine($"After episode 2 burst - Dropped: {droppedAfterEpisode2}, SlowConsumer: {slowConsumerAfterEpisode2}");

        // Cleanup
        await nats.PublishAsync("recovery.end", cancellationToken: cancellationToken);

        try
        {
            await subscription.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (TimeoutException)
        {
            _output.WriteLine("Subscription cleanup timed out");
        }

        // Assertions
        droppedAfterEpisode1.Should().BeGreaterThan(0, "messages should be dropped in episode 1");
        slowConsumerAfterEpisode1.Should().Be(1, "SlowConsumerDetected should fire once in episode 1");

        droppedAfterEpisode2.Should().BeGreaterThan(droppedAfterEpisode1, "more messages should be dropped in episode 2");
        slowConsumerAfterEpisode2.Should().Be(2, "SlowConsumerDetected should fire again after recovery");
    }
}
