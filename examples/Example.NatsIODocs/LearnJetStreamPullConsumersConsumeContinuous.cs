using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamPullConsumersConsumeContinuous(NatsServerFixture fixture, ITestOutputHelper output)
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
        await js.PublishAsync(subject: "orders.shipped", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");
        await js.PublishAsync(subject: "orders.shipped", data: """{"order_id":"ord_2zr9","customer":"globex"}""");

        await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("shipping")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
        });

        var shipped = 0;

        // NATS-DOC-START
        // Bind to the durable "shipping" consumer.
        var consumer = await js.GetConsumerAsync("ORDERS", "shipping");

        // ConsumeAsync sets up a continuous flow: it keeps pull requests open and
        // yields each order as soon as it lands in the stream. It runs until you
        // stop it, no fetch loop to write by hand.
        await foreach (var msg in consumer.ConsumeAsync<string>())
        {
            output.WriteLine($"shipping {msg.Data}");
            await msg.AckAsync();

            // A real consumer runs forever; stop once the backlog is clear so the
            // example returns.
            if (++shipped == 2)
            {
                break;
            }
        }

        // NATS-DOC-END
        Assert.Equal(2, shipped);
    }
}
