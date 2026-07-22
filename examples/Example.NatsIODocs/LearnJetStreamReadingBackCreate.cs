using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamReadingBackCreate(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });
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

        // Publish a few orders so the stream has something to read back
        await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_8w2k", Customer: "acme-co"));
        await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_2zr9", Customer: "globex"));
        await js.PublishAsync<Order>(subject: "orders.shipped", data: new Order(OrderId: "ord_8w2k", Customer: "acme-co"));

        // NATS-DOC-START
        // Create a durable consumer that delivers every stored message from the start
        var consumer = await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("billing")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
        });

        output.WriteLine($"Created durable consumer {consumer.Info.Config.Name} on stream ORDERS");

        // NATS-DOC-END
        Assert.Equal("billing", consumer.Info.Config.Name);
    }
}
