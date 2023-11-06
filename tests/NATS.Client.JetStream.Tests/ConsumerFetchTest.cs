using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ConsumerFetchTest
{
    private readonly ITestOutputHelper _output;

    public ConsumerFetchTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Fetch_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, opts: new NatsJSPubOpts { Serializer = TestDataJsonSerializer.Default }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await using var fc =
            await consumer.FetchAsync<TestData>(new NatsJSFetchOpts { MaxMsgs = 10, Serializer = TestDataJsonSerializer.Default }, cancellationToken: cts.Token);
        await foreach (var msg in fc.Msgs.ReadAllAsync(cts.Token))
        {
            await msg.AckAsync(new AckOpts(WaitUntilSent: true), cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            count++;
        }

        Assert.Equal(10, count);
    }

    [Fact]
    public async Task FetchNoWait_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, opts: new NatsJSPubOpts { Serializer = TestDataJsonSerializer.Default }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await foreach (var msg in consumer.FetchAllNoWaitAsync<TestData>(new NatsJSFetchOpts { MaxMsgs = 10, Serializer = TestDataJsonSerializer.Default }, cancellationToken: cts.Token))
        {
            await msg.AckAsync(new AckOpts(WaitUntilSent: true), cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            count++;
        }

        Assert.Equal(10, count);
    }

    [Fact]
    public async Task Fetch_dispose_test()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = NatsServer.StartJS();

        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var stream = await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        var consumer = await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = 10,
            IdleHeartbeat = TimeSpan.FromSeconds(5),
            Expires = TimeSpan.FromSeconds(10),
            Serializer = TestDataJsonSerializer.Default,
        };

        for (var i = 0; i < 100; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, opts: new NatsJSPubOpts { Serializer = TestDataJsonSerializer.Default }, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var fc = await consumer.FetchAsync<TestData>(fetchOpts, cancellationToken: cts.Token);

        var signal = new WaitSignal();
        var reader = Task.Run(async () =>
        {
            await foreach (var msg in fc.Msgs.ReadAllAsync(cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                signal.Pulse();

                // Introduce delay to make sure not all messages will be acked.
                await Task.Delay(1_000, cts.Token);
            }
        });

        await signal;
        await fc.DisposeAsync();

        await reader;

        var infos = new List<ConsumerInfo>();
        await foreach (var natsJSConsumer in stream.ListConsumersAsync(cts.Token))
        {
            infos.Add(natsJSConsumer.Info);
        }

        Assert.Single(infos);

        Assert.True(infos[0].NumAckPending > 0);
    }
}
