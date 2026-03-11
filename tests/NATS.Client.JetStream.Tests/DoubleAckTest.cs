using NATS.Client.Core2.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class DoubleAckTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public DoubleAckTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Fetch_should_not_block_socket()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 100; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        // fetch loop
        {
            var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

            var fetchOpts = new NatsJSFetchOpts
            {
                MaxMsgs = 100,
                Expires = TimeSpan.FromSeconds(5),
            };

            var count = 0;
            await foreach (var msg in consumer.FetchAsync<int>(opts: fetchOpts, cancellationToken: cts.Token))
            {
                // double ack will use the same TCP stream to wait for the ACK from the server
                // fetch must not block the socket so that the ACK can be received
                await msg.AckAsync(cancellationToken: cts.Token);
                count++;
            }

            Assert.Equal(100, count);
        }

        // consume loop
        {
            var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c2", cancellationToken: cts.Token);

            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 100,
                Expires = TimeSpan.FromSeconds(5),
            };

            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                // double ack will use the same TCP stream to wait for the ACK from the server
                // fetch must not block the socket so that the ACK can be received
                await msg.AckAsync(cancellationToken: cts.Token);
                count++;
            }

            Assert.Equal(100, count);
        }
    }
}
