using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamWorkerPoolRedeliveryCount(NatsServerFixture fixture, ITestOutputHelper output)
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

        var read = 0;

        // NATS-DOC-START
        // Bind to the durable "shipping" consumer.
        var consumer = await js.GetConsumerAsync("ORDERS", "shipping");

        // Each message reports how many times it has been delivered. A count
        // above one means a redelivery: the server handed this order out before,
        // but a worker crashed or ran past AckWait before acking. Key your side
        // effects by order_id so handling the same order twice is harmless.
        await foreach (var msg in consumer.FetchAsync<string>(
                           opts: new NatsJSFetchOpts { MaxMsgs = 10, Expires = TimeSpan.FromSeconds(2) }))
        {
            var delivered = msg.Metadata?.NumDelivered ?? 0;
            if (delivered > 1)
            {
                output.WriteLine($"redelivery #{delivered} of {msg.Data}");
            }
            else
            {
                output.WriteLine($"first delivery of {msg.Data}");
            }

            await msg.AckAsync();
            read++;
        }

        // NATS-DOC-END
        Assert.Equal(2, read);
    }
}
