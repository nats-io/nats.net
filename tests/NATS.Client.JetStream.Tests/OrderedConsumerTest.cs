using System.Diagnostics;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.Platform.Windows.Tests;
using NATS.Client.TestUtilities2;

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

        for (var i = 0; i < 50; i++)
        {
            if (i % 10 == 0)
            {
                server = await server.RestartAsync();
            }

            (await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token)).EnsureSuccess();
        }

        (await js.PublishAsync("s1.foo", -1, cancellationToken: cts.Token)).EnsureSuccess();

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
}
