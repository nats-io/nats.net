using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ConsumerNextTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public ConsumerNextTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Next_test()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestTimeout = TimeSpan.FromSeconds(10) });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
            var next = await consumer.NextAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            if (next is { } msg)
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                Assert.Equal(i, msg.Data!.Test);
            }
        }
    }
}
