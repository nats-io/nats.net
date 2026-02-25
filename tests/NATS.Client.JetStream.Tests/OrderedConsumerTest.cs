using System.Diagnostics;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class OrderedConsumerTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public OrderedConsumerTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Consume_test()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        var count = 0;
        var consumeOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 3,
            Expires = TimeSpan.FromSeconds(3),
        };
        await foreach (var msg in consumer.ConsumeAsync<int>(opts: consumeOpts, cancellationToken: cts.Token))
        {
            Assert.Equal(count, msg.Data);
            if (++count == 10)
                break;
        }
    }

    [Fact]
    public async Task Consume_reconnect_publish()
    {
        var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestTimeout = TimeSpan.FromSeconds(10) });
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync("s1", ["s1.*"], cts.Token);

        async Task PublishWithId(int i)
        {
            for (var j = 0; j < 3; j++)
            {
                PubAckResponse ack;
                try
                {
                    ack = await js.PublishAsync("s1.foo", i, opts: new NatsJSPubOpts { MsgId = $"{i}" }, cancellationToken: cts.Token);
                }
                catch (NatsException)
                {
                    await Task.Delay(100, cts.Token);
                    continue;
                }

                if (ack.IsSuccess())
                    return;
                await Task.Delay(100, cts.Token);
                if (ack.Duplicate)
                    break;
                ack.EnsureSuccess();
            }

            throw new Exception("Publish failed after retries");
        }

        for (var i = 0; i < 50; i++)
        {
            if (i % 10 == 0)
            {
                server = await server.RestartAsync();
            }

            await PublishWithId(i);
        }

        await PublishWithId(-1);

        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        var consumeOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 3,
            Expires = TimeSpan.FromSeconds(3),
        };
        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<int>(opts: consumeOpts, cancellationToken: cts.Token))
        {
            if (msg.Data == -1)
                break;
            Assert.Equal(count, msg.Data);
            count++;
        }

        Assert.Equal(50, count);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task Fetch_test()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        for (var i = 0; i < 10;)
        {
            var fetchOpts = new NatsJSFetchOpts
            {
                MaxMsgs = 3,
                Expires = TimeSpan.FromSeconds(3),
            };
            await foreach (var msg in consumer.FetchAsync<int>(opts: fetchOpts, cancellationToken: cts.Token))
            {
                Assert.Equal(i, msg.Data);
                i++;
            }
        }
    }

    [Fact]
    public async Task Fetch_no_wait_test()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        // Where there is no data, we should not wait for the timeout.
        var count = 0;
        var stopwatch = Stopwatch.StartNew();
        await foreach (var msg in consumer.FetchNoWaitAsync<int>(opts: new NatsJSFetchOpts { MaxMsgs = 10 }, cancellationToken: cts.Token))
        {
            count++;
        }

        stopwatch.Stop();

        Assert.Equal(0, count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));

        // Where there is less than we want to fetch, we should get all the messages
        // without waiting for the timeout.
        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        stopwatch.Restart();
        var iterationCount = 0;
        while (!cts.IsCancellationRequested)
        {
            var currentCount = 0;
            await foreach (var msg in consumer.FetchNoWaitAsync<int>(opts: new NatsJSFetchOpts { MaxMsgs = 6 }, cancellationToken: cts.Token))
            {
                Assert.Equal(count, msg.Data);
                count++;
                currentCount++;
            }

            // no data
            if (currentCount == 0)
            {
                break;
            }

            iterationCount++;
        }

        stopwatch.Stop();

        Assert.Equal(2, iterationCount);
        Assert.Equal(10, count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Next_test()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        for (var i = 0; i < 10;)
        {
            var nextOpts = new NatsJSNextOpts
            {
                Expires = TimeSpan.FromSeconds(3),
            };
            var next = await consumer.NextAsync<int>(opts: nextOpts, cancellationToken: cts.Token);

            if (next is { } msg)
            {
                Assert.Equal(i, msg.Data);
                i++;
            }
        }
    }

    [Fact]
    public async Task Ordered_consume_connection_failed_test()
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
        var stream = await js.CreateStreamAsync("s1", ["s1.*"], cts.Token);

        // Publish some messages
        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        // Start consuming in background
        var consumeTask = Task.Run(async () =>
        {
            await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
            {
                // Let messages flow through
            }
        });

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
    public async Task Fetch_recovers_from_consumer_deletion()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        // Publish 20 messages
        for (var i = 0; i < 20; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        var count = 0;
        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = 5,
            Expires = TimeSpan.FromSeconds(3),
        };

        await foreach (var msg in consumer.FetchAsync<int>(opts: fetchOpts, cancellationToken: cts.Token))
        {
            Assert.Equal(count, msg.Data);
            count++;

            // After receiving 3 messages, delete the consumer to force sequence mismatch
            if (count == 3)
            {
                var consumerName = consumer.Info.Name;
                _output.WriteLine($"Deleting consumer {consumerName} after message {count}");
                await js.DeleteConsumerAsync($"{prefix}s1", consumerName, cts.Token);
            }
        }

        // Should have received all 5 messages despite consumer deletion
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task Consume_recovers_from_consumer_deletion()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        // Publish 20 messages
        for (var i = 0; i < 20; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        var count = 0;
        var consumeOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 3,
            Expires = TimeSpan.FromSeconds(3),
        };

        await foreach (var msg in consumer.ConsumeAsync<int>(opts: consumeOpts, cancellationToken: cts.Token))
        {
            Assert.Equal(count, msg.Data);
            count++;

            // After receiving 8 messages, delete the consumer to force sequence mismatch
            if (count == 8)
            {
                var consumerName = consumer.Info.Name;
                _output.WriteLine($"Deleting consumer {consumerName} after message {count}");
                await js.DeleteConsumerAsync($"{prefix}s1", consumerName, cts.Token);
            }

            if (count == 20)
                break;
        }

        // Should have received all 20 messages despite consumer deletion
        Assert.Equal(20, count);
    }

    [Fact]
    public async Task FetchNoWait_recovers_from_consumer_deletion()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        // Publish 15 messages
        for (var i = 0; i < 15; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        var count = 0;
        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = 10,
        };

        await foreach (var msg in consumer.FetchNoWaitAsync<int>(opts: fetchOpts, cancellationToken: cts.Token))
        {
            Assert.Equal(count, msg.Data);
            count++;

            // After receiving 5 messages, delete the consumer to force sequence mismatch
            if (count == 5)
            {
                var consumerName = consumer.Info.Name;
                _output.WriteLine($"Deleting consumer {consumerName} after message {count}");
                await js.DeleteConsumerAsync($"{prefix}s1", consumerName, cts.Token);
            }
        }

        // Should have received all 10 messages despite consumer deletion
        Assert.Equal(10, count);
    }

    [Fact]
    public async Task Fetch_maxbytes_with_consumer_deletion()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        // Publish 20 messages
        for (var i = 0; i < 20; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = (NatsJSOrderedConsumer)await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        var count = 0;
        var fetchOpts = new NatsJSFetchOpts
        {
            MaxBytes = 1024, // Use MaxBytes instead of MaxMsgs
            Expires = TimeSpan.FromSeconds(3),
        };

        await foreach (var msg in consumer.FetchAsync<int>(opts: fetchOpts, cancellationToken: cts.Token))
        {
            Assert.Equal(count, msg.Data);
            count++;

            // After receiving 5 messages, delete the consumer to force sequence mismatch
            if (count == 5)
            {
                var consumerName = consumer.Info.Name;
                _output.WriteLine($"Deleting consumer {consumerName} after message {count}");
                await js.DeleteConsumerAsync($"{prefix}s1", consumerName, cts.Token);
            }
        }

        // Should have received messages up to MaxBytes limit, with recovery after deletion
        _output.WriteLine($"Received {count} messages with MaxBytes mode");
        Assert.True(count > 5, "Should have received messages after consumer deletion");
    }
}
