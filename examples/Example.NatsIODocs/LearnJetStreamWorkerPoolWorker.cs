using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamWorkerPoolWorker(NatsServerFixture fixture, ITestOutputHelper output)
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

        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // A handful of orders for the pool to work through
        await js.PublishAsync<Order>(subject: "orders.shipped", data: new Order(OrderId: "ord_8w2k", Customer: "acme-co"));
        await js.PublishAsync<Order>(subject: "orders.shipped", data: new Order(OrderId: "ord_2zr9", Customer: "globex"));
        await js.PublishAsync<Order>(subject: "orders.shipped", data: new Order(OrderId: "ord_5k1m", Customer: "initech"));

        await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("shipping")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
        });

        var shipped = 0;

        // NATS-DOC-START
        // Bind to the durable "shipping" consumer created earlier.
        var consumer = await js.GetConsumerAsync("ORDERS", "shipping");

        // ConsumeAsync yields the orders the server hands this worker. Run this
        // same program in several processes: they all share the one "shipping"
        // consumer, and the server splits the stored orders across them, one
        // order to one worker.
        await foreach (var msg in consumer.ConsumeAsync<Order>())
        {
            output.WriteLine($"shipping {msg.Data}");
            await msg.AckAsync();

            // A real worker loops forever; stop once the backlog is clear so the
            // example returns.
            if (++shipped == 3)
            {
                break;
            }
        }

        // NATS-DOC-END
        Assert.Equal(3, shipped);
    }
}
