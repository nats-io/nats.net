using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamFilteringCreateFiltered(NatsServerFixture fixture, ITestOutputHelper output)
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

        // The ORDERS stream already holds orders.created and orders.shipped messages
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));
        await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_8w2k", Customer: "acme-co"));
        await js.PublishAsync<Order>(subject: "orders.shipped", data: new Order(OrderId: "ord_8w2k", Customer: "acme-co"));
        await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_2zr9", Customer: "globex"));
        await js.PublishAsync<Order>(subject: "orders.shipped", data: new Order(OrderId: "ord_2zr9", Customer: "globex"));

        var subjects = new List<string>();

        // NATS-DOC-START
        // Create a durable pull consumer that only sees orders.shipped
        var consumer = await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("analytics")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = "orders.shipped",
        });

        output.WriteLine($"Created durable consumer {consumer.Info.Config.Name} filtered on orders.shipped");

        // Fetch a small batch; only orders.shipped comes back
        await foreach (var msg in consumer.FetchAsync<Order>(opts: new NatsJSFetchOpts { MaxMsgs = 5, Expires = TimeSpan.FromSeconds(2) }))
        {
            output.WriteLine($"{msg.Subject}: {msg.Data}");
            subjects.Add(msg.Subject);
            await msg.AckAsync();
        }

        // NATS-DOC-END
        Assert.Equal("analytics", consumer.Info.Config.Name);
        Assert.All(subjects, subject => Assert.Equal("orders.shipped", subject));
    }
}
