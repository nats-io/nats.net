using NATS.Client.Core.Tests;
using NATS.Client.Platform.Windows.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

public class ConsumerNextTest
{
    private readonly ITestOutputHelper _output;

    public ConsumerNextTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Next_test()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestTimeout = TimeSpan.FromSeconds(10) });
        await nats.ConnectRetryAsync();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
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
