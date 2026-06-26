using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamYourFirstConsumerDoubleAck(NatsServerFixture fixture, ITestOutputHelper output)
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

        // Publish an order so the consumer has something to pull
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");

        // Create the durable pull consumer to read from
        await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("shipping")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
        });

        // NATS-DOC-START
        // Bind to the existing durable consumer
        var consumer = await js.GetConsumerAsync("ORDERS", "shipping");

        // Pull one message and process it
        var msg = await consumer.NextAsync<string>();
        if (msg is { } order)
        {
            output.WriteLine($"{order.Subject}: {order.Data}");

            // Double-ack: wait for the server to confirm the acknowledgment was stored
            await order.AckAsync(new AckOpts { DoubleAck = true });
        }

        // NATS-DOC-END
        Assert.NotNull(msg);
    }
}
