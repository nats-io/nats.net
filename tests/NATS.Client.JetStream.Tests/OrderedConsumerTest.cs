using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class OrderedConsumerTest
{
    private readonly ITestOutputHelper _output;

    public OrderedConsumerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Consume_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var stream = await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        var count = 0;
        _output.WriteLine("Consuming...");
        var consumeOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 3,
            Expires = TimeSpan.FromSeconds(3),
        };
        await foreach (var msg in consumer.ConsumeAsync<int>(opts: consumeOpts, cancellationToken: cts.Token))
        {
            _output.WriteLine($"[RCV] {msg.Data}");
            Assert.Equal(count, msg.Data);
            if (++count == 10)
                break;
        }
    }

    [Fact]
    public async Task Consume_reconnect_publish()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        for (var i = 0; i < 100; i++)
        {
            if (i % 10 == 0)
            {
                await server.RestartAsync();
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

        Assert.Equal(100, count);
    }

    [Fact]
    public async Task Fetch_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var stream = await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        for (var i = 0; i < 10;)
        {
            _output.WriteLine("Fetching...");
            var fetchOpts = new NatsJSFetchOpts
            {
                MaxMsgs = 3,
                Expires = TimeSpan.FromSeconds(3),
            };
            await foreach (var msg in consumer.FetchAsync<int>(opts: fetchOpts, cancellationToken: cts.Token))
            {
                _output.WriteLine($"[RCV] {msg.Data}");
                Assert.Equal(i, msg.Data);
                i++;
            }
        }
    }

    [Fact]
    public async Task Next_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var stream = await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        for (var i = 0; i < 10; i++)
        {
            await js.PublishAsync("s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        for (var i = 0; i < 10;)
        {
            _output.WriteLine("Next...");
            var nextOpts = new NatsJSNextOpts
            {
                Expires = TimeSpan.FromSeconds(3),
            };
            var next = await consumer.NextAsync<int>(opts: nextOpts, cancellationToken: cts.Token);

            if (next is { } msg)
            {
                _output.WriteLine($"[RCV] {msg.Data}");
                Assert.Equal(i, msg.Data);
                i++;
            }
        }
    }
}
