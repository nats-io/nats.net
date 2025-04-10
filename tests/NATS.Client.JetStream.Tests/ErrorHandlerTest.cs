using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ErrorHandlerTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;
    private int _timeoutNotifications = 0;
    private int _count = 0;

    public ErrorHandlerTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Consumer_fetch_error_handling()
    {
        var proxy = new NatsProxy(_server.Port);
        await using var nats = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{proxy.Port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", new[] { $"{prefix}s1.*" }), cts.Token);
        var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig($"{prefix}c1"), cts.Token);

        (await js.PublishAsync($"{prefix}s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

        var timeoutNotifications = 0;
        var opts = new NatsJSNextOpts
        {
            NotificationHandler = (e, _) =>
            {
                if (e is NatsJSTimeoutNotification)
                {
                    Interlocked.Increment(ref timeoutNotifications);
                }

                return Task.CompletedTask;
            },
            Expires = TimeSpan.FromSeconds(6),
            IdleHeartbeat = TimeSpan.FromSeconds(3),
        };

        // Next is fetch under the hood.
        var next = await consumer.NextAsync<int>(opts: opts, cancellationToken: cts.Token);
        if (next is { } msg)
        {
            msg.Subject.Should().Be($"{prefix}s1.1");
            msg.Data.Should().Be(1);
            await msg.AckAsync(cancellationToken: cts.Token);
        }
        else
        {
            Assert.Fail("No message received.");
        }

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        // Create an empty stream to potentially reduce the chance of having a message.
        var stream2 = await js.CreateStreamAsync(new StreamConfig($"{prefix}s2", new[] { $"{prefix}s2.*" }), cts.Token);
        var consumer2 = await stream2.CreateOrUpdateConsumerAsync(new ConsumerConfig($"{prefix}c2"), cts.Token);

        // reduce heartbeat time out to increase the chance of receiving notification.
        var opts2 = new NatsJSNextOpts
        {
            NotificationHandler = (e, _) =>
            {
                if (e is NatsJSTimeoutNotification)
                {
                    Interlocked.Increment(ref timeoutNotifications);
                }

                return Task.CompletedTask;
            },
            Expires = TimeSpan.FromSeconds(5),
            IdleHeartbeat = TimeSpan.FromSeconds(1),
        };

        var next2 = await consumer2.NextAsync<int>(opts: opts2, cancellationToken: cts.Token);
        Assert.Null(next2);
        Assert.Equal(1, Volatile.Read(ref timeoutNotifications));
    }

    [Fact]
    public async Task Consumer_consume_handling()
    {
        var proxy = new NatsProxy(_server.Port);
        await using var nats = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{proxy.Port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", new[] { $"{prefix}s1.*" }), cts.Token);
        var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig($"{prefix}c1"), cts.Token);

        (await js.PublishAsync($"{prefix}s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

        var opts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
            NotificationHandler = (e, _) =>
            {
                _output.WriteLine($"Got notification (type:{e.GetType().Name}): {e}");
                if (e is NatsJSTimeoutNotification)
                {
                    Interlocked.Increment(ref _timeoutNotifications);
                }

                return Task.CompletedTask;
            },

            // keep this high to avoid resetting heartbeat timer with
            // 408 request timeout messages
            Expires = TimeSpan.FromSeconds(60),

            IdleHeartbeat = TimeSpan.FromSeconds(3),
        };

        await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
        {
            _output.WriteLine($">> Consumed message (1): {msg.Data}");
            msg.Data.Should().Be(1);
            msg.Subject.Should().Be($"{prefix}s1.1");
            await msg.AckAsync(cancellationToken: cts.Token);
            break;
        }

        Assert.Equal(0, Interlocked.CompareExchange(ref _timeoutNotifications, 0, 0));

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m =>
        {
            var isIdleHeartbeat = m?.Contains("Idle Heartbeat") ?? false;

            if (isIdleHeartbeat)
            {
                _output.WriteLine($">> Swallowed Idle Heartbeat: {m}");
                return null;
            }
            else
            {
                return m;
            }
        });

        var consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var consume = Task.Run(
            async () =>
            {
                await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: consumeCts.Token))
                {
                    _output.WriteLine($">> Consumed message (2): {msg.Data}");
                    Interlocked.Increment(ref _count);
                    await msg.AckAsync(cancellationToken: consumeCts.Token);
                }
            },
            cts.Token);

        await Retry.Until(
            reason: "timed out",
            condition: () => Interlocked.CompareExchange(ref _timeoutNotifications, 0, 0) > 0,
            timeout: TimeSpan.FromSeconds(60));

        consumeCts.Cancel();

        await consume;

        Assert.Equal(0, Interlocked.CompareExchange(ref _count, 0, 0));
        Assert.True(Interlocked.CompareExchange(ref _timeoutNotifications, 0, 0) > 0);
    }

    [Fact]
    public async Task Ordered_consumer_fetch_error_handling()
    {
        var proxy = new NatsProxy(_server.Port);
        await using var nats = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{proxy.Port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", new[] { $"{prefix}s1.*" }), cts.Token);
        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        (await js.PublishAsync($"{prefix}s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

        var timeoutNotifications = 0;
        var opts = new NatsJSFetchOpts
        {
            MaxMsgs = 10,
            NotificationHandler = (e, _) =>
            {
                if (e is NatsJSTimeoutNotification)
                {
                    Interlocked.Increment(ref timeoutNotifications);
                }

                return Task.CompletedTask;
            },
            Expires = TimeSpan.FromSeconds(6),
            IdleHeartbeat = TimeSpan.FromSeconds(3),
        };

        var count1 = 0;
        await foreach (var msg in consumer.FetchAsync<int>(opts: opts, cancellationToken: cts.Token))
        {
            msg.Subject.Should().Be($"{prefix}s1.1");
            msg.Data.Should().Be(1);
            await msg.AckAsync(cancellationToken: cts.Token);
            count1++;
        }

        Assert.Equal(1, count1);

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        // Create an empty stream since ordered consumer will pick up messages from beginning everytime.
        var stream2 = await js.CreateStreamAsync(new StreamConfig($"{prefix}s2", new[] { $"{prefix}s2.*" }), cts.Token);
        var consumer2 = (NatsJSOrderedConsumer)await stream2.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        // reduce heartbeat time out to increase the chance of receiving notification.
        var opts2 = new NatsJSFetchOpts
        {
            MaxMsgs = 10,
            NotificationHandler = (e, _) =>
            {
                if (e is NatsJSTimeoutNotification)
                {
                    Interlocked.Increment(ref timeoutNotifications);
                }

                return Task.CompletedTask;
            },
            Expires = TimeSpan.FromSeconds(5),
            IdleHeartbeat = TimeSpan.FromSeconds(1),
        };

        var count = 0;
        await foreach (var unused in consumer2.FetchAsync<int>(opts: opts2, cancellationToken: cts.Token))
        {
            count++;
        }

        Assert.Equal(0, count);
        Assert.Equal(1, Volatile.Read(ref timeoutNotifications));
    }

    [Fact]
    public async Task Ordered_consumer_consume_handling()
    {
        var proxy = new NatsProxy(_server.Port);
        await using var nats = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{proxy.Port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        var prefix = _server.GetNextId();

        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var stream = await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", new[] { $"{prefix}s1.*" }), cts.Token);
        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        (await js.PublishAsync($"{prefix}s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

        var timeoutNotifications = 0;
        var opts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
            NotificationHandler = (e, _) =>
            {
                if (e is NatsJSTimeoutNotification)
                {
                    Interlocked.Increment(ref timeoutNotifications);
                }

                return Task.CompletedTask;
            },
            Expires = TimeSpan.FromSeconds(6),
            IdleHeartbeat = TimeSpan.FromSeconds(3),
        };

        await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
        {
            msg.Data.Should().Be(1);
            msg.Subject.Should().Be($"{prefix}s1.1");
            break;
        }

        Assert.Equal(0, Volatile.Read(ref timeoutNotifications));

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        var consumeCts = new CancellationTokenSource();
        var consume = Task.Run(
            async () =>
            {
                // reduce heartbeat time out to increase the chance of receiving notification.
                var opts2 = new NatsJSConsumeOpts
                {
                    MaxMsgs = 10,
                    NotificationHandler = (e, _) =>
                    {
                        if (e is NatsJSTimeoutNotification)
                        {
                            Interlocked.Increment(ref timeoutNotifications);
                        }

                        return Task.CompletedTask;
                    },
                    Expires = TimeSpan.FromSeconds(6),
                    IdleHeartbeat = TimeSpan.FromSeconds(1),
                };

                await foreach (var unused in consumer.ConsumeAsync<int>(opts: opts2, cancellationToken: consumeCts.Token))
                {
                }
            },
            cts.Token);

        await Retry.Until("timed out", () => Volatile.Read(ref timeoutNotifications) > 0, timeout: TimeSpan.FromSeconds(30));
        consumeCts.Cancel();

        Assert.True(Volatile.Read(ref timeoutNotifications) > 0);

        try
        {
            await consume;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task Exception_propagation_handling()
    {
        var proxy = new NatsProxy(_server.Port);
        await using var nats = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{proxy.Port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", new[] { $"{prefix}s1.*" }), cts.Token);

        // reduce heartbeat time out to increase the chance of receiving notification.
        var opts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
            NotificationHandler = (_, _) => throw new TestConsumerNotificationException(),
            Expires = TimeSpan.FromSeconds(6),
            IdleHeartbeat = TimeSpan.FromSeconds(1),
        };

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        try
        {
            var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig($"{prefix}c1"), cts.Token);
            await foreach (var unused in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
            }

            throw new Exception("Should have thrown (consumer)");
        }
        catch (TestConsumerNotificationException)
        {
        }

        try
        {
            var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);
            await foreach (var unused in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
            }

            throw new Exception("Should have thrown (ordered consumer)");
        }
        catch (TestConsumerNotificationException)
        {
        }
    }
}

public class TestConsumerNotificationException : Exception
{
}
