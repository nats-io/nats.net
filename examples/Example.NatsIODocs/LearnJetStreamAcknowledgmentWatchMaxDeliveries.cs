using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamAcknowledgmentWatchMaxDeliveries(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Start from a clean stream (the test server is shared across the collection)
        try
        {
            await js.DeleteStreamAsync("ORDERS");
        }
        catch (NatsJSApiException)
        {
            // Stream doesn't exist yet, nothing to delete
        }

        // The ORDERS stream captures every subject under `orders.`
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // A durable pull consumer that gives up after two delivery attempts
        await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("shipping")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            MaxDeliver = 2,
            AckWait = TimeSpan.FromSeconds(1),
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var watcher = Task.Run(async () =>
        {
            // NATS-DOC-START
            // Watch for messages a consumer gave up on after hitting MaxDeliver
            var subject = "$JS.EVENT.ADVISORY.CONSUMER.MAX_DELIVERIES.ORDERS.shipping";
            await foreach (var advisory in client.SubscribeAsync<string>(subject, cancellationToken: cts.Token))
            {
                output.WriteLine($"max deliveries reached: {advisory.Data}");
            }

            // NATS-DOC-END
        });

        // Publish an order, then keep nak-ing it until the server fires the advisory
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");

        var consumer = await js.GetConsumerAsync("ORDERS", "shipping");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var msg = await consumer.NextAsync<string>(opts: new NatsJSNextOpts { Expires = TimeSpan.FromSeconds(2) });
            if (msg is { } order)
            {
                await order.NakAsync();
            }
        }

        await Task.Delay(2000);
        await cts.CancelAsync();

        try
        {
            await watcher;
        }
        catch (OperationCanceledException)
        {
            // Subscription stopped when the watch window closed
        }
    }
}
