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
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = (NatsJSConsumer)await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await using var fc =
            await consumer.FetchInternalAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, new NatsJSFetchOpts { MaxMsgs = 10 }, cancellationToken: cts.Token);
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
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = (NatsJSConsumer)await js.GetConsumerAsync("s1", "c1", cts.Token);
        var count = 0;
        await foreach (var msg in consumer.FetchNoWaitAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, new NatsJSFetchOpts { MaxMsgs = 10 }, cancellationToken: cts.Token))
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
        var consumer = (NatsJSConsumer)await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = 10,
            IdleHeartbeat = TimeSpan.FromSeconds(5),
            Expires = TimeSpan.FromSeconds(10),
        };

        for (var i = 0; i < 100; i++)
        {
            var ack = await js.PublishAsync("s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var fc = await consumer.FetchInternalAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, fetchOpts, cancellationToken: cts.Token);

        var signal1 = new WaitSignal();
        var signal2 = new WaitSignal();
        var reader = Task.Run(async () =>
        {
            await foreach (var msg in fc.Msgs.ReadAllAsync(cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                signal1.Pulse();
                await signal2;
            }
        });

        await signal1;

        // Dispose waits for all the pending messages to be delivered to the loop
        // since the channel reader carries on reading the messages in its internal queue.
        await fc.DisposeAsync();

        // At this point we should only have ACKed one message
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(9, consumer.Info.NumAckPending);

        signal2.Pulse();

        await reader;

        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }
}
