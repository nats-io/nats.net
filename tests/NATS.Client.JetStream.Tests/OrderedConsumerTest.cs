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
        await foreach (var msg in consumer.ConsumeAllAsync<int>(consumeOpts, cancellationToken: cts.Token))
        {
            _output.WriteLine($"[RCV] {msg.Data}");
            Assert.Equal(count, msg.Data);
            if (++count == 10)
                break;
        }
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
            await foreach (var msg in consumer.FetchAllAsync<int>(fetchOpts, cancellationToken: cts.Token))
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
            var next = await consumer.NextAsync<int>(nextOpts, cts.Token);

            if (next is { } msg)
            {
                _output.WriteLine($"[RCV] {msg.Data}");
                Assert.Equal(i, msg.Data);
                i++;
            }
        }
    }
}
