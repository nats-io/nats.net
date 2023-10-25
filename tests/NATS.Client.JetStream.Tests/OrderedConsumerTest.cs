using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class OrderedConsumerTest
{
    private readonly ITestOutputHelper _output;

    public OrderedConsumerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_ordered_consumer_and_fetch()
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
}
