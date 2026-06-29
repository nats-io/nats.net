using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamOrderedConsumerRead(NatsServerFixture fixture, ITestOutputHelper output)
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

        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");
        await js.PublishAsync(subject: "orders.shipped", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_2zr9","customer":"globex"}""");

        var read = 0;

        // NATS-DOC-START
        // Ask for an ordered consumer over the stream. There's no ack to send:
        // the library runs the consumer for you and recreates it if it ever
        // misses a message, so you read every order in stream order.
        var consumer = await js.CreateOrderedConsumerAsync("ORDERS");

        // Read the whole log once, in order, stopping when caught up.
        await foreach (var msg in consumer.ConsumeAsync<string>())
        {
            output.WriteLine($"order {msg.Data}");
            read++;
            if (msg.Metadata?.NumPending == 0)
            {
                break;
            }
        }

        // NATS-DOC-END
        Assert.Equal(3, read);
    }
}
