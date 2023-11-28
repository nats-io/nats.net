using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ErrorHandlerTest
{
    private readonly ITestOutputHelper _output;

    public ErrorHandlerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Consumer_fetch_error_handling()
    {
        await using var server = NatsServer.StartJS();
        var (nats1, proxy) = server.CreateProxiedClientConnection();
        await using var nats = nats1;
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig("s1", new[] { "s1.*" }), cts.Token);
        var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("c1"), cts.Token);

        (await js.PublishAsync("s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

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
            msg.Subject.Should().Be("s1.1");
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
        var stream2 = await js.CreateStreamAsync(new StreamConfig("s2", new[] { "s2.*" }), cts.Token);
        var consumer2 = await stream2.CreateOrUpdateConsumerAsync(new ConsumerConfig("c2"), cts.Token);

        var next2 = await consumer2.NextAsync<int>(opts: opts, cancellationToken: cts.Token);
        Assert.Null(next2);
        Assert.Equal(1, Volatile.Read(ref timeoutNotifications));
    }

    [Fact]
    public async Task Consumer_consume_handling()
    {
        await using var server = NatsServer.StartJS();
        var (nats1, proxy) = server.CreateProxiedClientConnection();
        await using var nats = nats1;
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig("s1", new[] { "s1.*" }), cts.Token);
        var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("c1"), cts.Token);

        (await js.PublishAsync("s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

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
            msg.Subject.Should().Be("s1.1");
            await msg.AckAsync(cancellationToken: cts.Token);
            break;
        }

        Assert.Equal(0, Volatile.Read(ref timeoutNotifications));

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        var count = 0;
        var consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var consume = Task.Run(
            async () =>
            {
                await foreach (var unused in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: consumeCts.Token))
                {
                    Interlocked.Increment(ref count);
                }
            },
            cts.Token);

        await Retry.Until("timed out", () => Volatile.Read(ref timeoutNotifications) > 0, timeout: TimeSpan.FromSeconds(20));
        consumeCts.Cancel();
        await consume;

        Assert.Equal(0, Volatile.Read(ref count));
        Assert.True(Volatile.Read(ref timeoutNotifications) > 0);
    }

    [Fact]
    public async Task Ordered_consumer_fetch_error_handling()
    {
        await using var server = NatsServer.StartJS();
        var (nats1, proxy) = server.CreateProxiedClientConnection();
        await using var nats = nats1;
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig("s1", new[] { "s1.*" }), cts.Token);
        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        (await js.PublishAsync("s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

        var timeoutNotifications = 0;
        var opts = new NatsJSFetchOpts(10)
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

        var count1 = 0;
        await foreach (var msg in consumer.FetchAsync<int>(opts: opts, cancellationToken: cts.Token))
        {
            msg.Subject.Should().Be("s1.1");
            msg.Data.Should().Be(1);
            await msg.AckAsync(cancellationToken: cts.Token);
            count1++;
        }

        Assert.Equal(1, count1);

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        // Create an empty stream since ordered consumer will pick up messages from beginning everytime.
        var stream2 = await js.CreateStreamAsync(new StreamConfig("s2", new[] { "s2.*" }), cts.Token);
        var consumer2 = (NatsJSOrderedConsumer)await stream2.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        var count = 0;
        await foreach (var unused in consumer2.FetchAsync<int>(opts: opts, cancellationToken: cts.Token))
        {
            count++;
        }

        Assert.Equal(0, count);
        Assert.Equal(1, Volatile.Read(ref timeoutNotifications));
    }

    [Fact]
    public async Task Ordered_consumer_consume_handling()
    {
        await using var server = NatsServer.StartJS();
        var (nats1, proxy) = server.CreateProxiedClientConnection();
        await using var nats = nats1;
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig("s1", new[] { "s1.*" }), cts.Token);
        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        (await js.PublishAsync("s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

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
            msg.Subject.Should().Be("s1.1");
            break;
        }

        Assert.Equal(0, Volatile.Read(ref timeoutNotifications));

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        var consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var consume = Task.Run(
            async () =>
            {
                await foreach (var unused in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: consumeCts.Token))
                {
                }
            },
            cts.Token);

        await Retry.Until("timed out", () => Volatile.Read(ref timeoutNotifications) > 0, timeout: TimeSpan.FromSeconds(20));
        consumeCts.Cancel();
        await consume;

        Assert.True(Volatile.Read(ref timeoutNotifications) > 0);
    }

    [Fact]
    public async Task Exception_propagation_handling()
    {
        await using var server = NatsServer.StartJS();
        var (nats1, proxy) = server.CreateProxiedClientConnection();
        await using var nats = nats1;
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig("s1", new[] { "s1.*" }), cts.Token);

        var opts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
            NotificationHandler = (_, _) => throw new TestConsumerNotificationException(),
            Expires = TimeSpan.FromSeconds(6),
            IdleHeartbeat = TimeSpan.FromSeconds(3),
        };

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        try
        {
            var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("c1"), cts.Token);
            await foreach (var unused in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
            }

            throw new Exception("Should have thrown");
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

            throw new Exception("Should have thrown");
        }
        catch (TestConsumerNotificationException)
        {
        }
    }
}

public class TestConsumerNotificationException : Exception
{
}
