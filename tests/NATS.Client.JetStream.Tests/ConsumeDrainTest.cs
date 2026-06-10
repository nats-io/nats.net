using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ConsumeDrainTest
{
    private readonly NatsServerFixture _server;

    public ConsumeDrainTest(NatsServerFixture server) => _server = server;

    [Fact]
    public async Task Consume_drain_on_cancel_delivers_buffered_and_keeps_connection()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        const int total = 100;
        for (var i = 0; i < total; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        // MaxMsgs above the published count so a single pull buffers all messages.
        var consumerOpts = new NatsJSConsumeOpts { MaxMsgs = 100, DrainOnCancel = true };
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);

        var received = 0;
        await foreach (var msg in consumer.ConsumeAsync(serializer: TestDataJsonSerializer<TestData>.Default, consumerOpts, cancellationToken: cts.Token))
        {
            // Simulate some processing time.
            await Task.Delay(10, CancellationToken.None);

            // ACK after cancellation must still succeed since the connection stays open.
            await msg.AckAsync(cancellationToken: CancellationToken.None);
            received++;

            // Cancel once consumption is under way; drain must still deliver the
            // remaining buffered/in-flight messages instead of dropping them.
            if (received == 1)
#if NET8_0_OR_GREATER
                await cts.CancelAsync();
#else
                cts.Cancel();
#endif
        }

        Assert.Equal(total, received);

        // Connection is left usable after the drain completes.
        Assert.Equal(NatsConnectionState.Open, nats.ConnectionState);
        await nats.PingAsync(CancellationToken.None);
        var ack2 = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = total }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: CancellationToken.None);
        ack2.EnsureSuccess();
    }

    [Fact]
    public async Task Consume_without_drain_on_cancel_drops_buffered_messages()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        const int total = 100;
        for (var i = 0; i < total; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        // DrainOnCancel defaults to false: cancelling stops promptly and does not
        // deliver the messages still buffered in the consumer.
        var consumerOpts = new NatsJSConsumeOpts { MaxMsgs = 100 };
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);

        var received = 0;
        await foreach (var msg in consumer.ConsumeAsync(serializer: TestDataJsonSerializer<TestData>.Default, consumerOpts, cancellationToken: cts.Token))
        {
            await msg.AckAsync(cancellationToken: CancellationToken.None);
            received++;

            if (received == 1)
#if NET8_0_OR_GREATER
                await cts.CancelAsync();
#else
                cts.Cancel();
#endif
        }

        // Without drain the loop stops promptly on cancel (fired after the first
        // message) and abandons the buffered messages rather than delivering them.
        Assert.True(received <= 5, $"expected a prompt stop (<= 5 messages), got {received} of {total}");
    }
}
