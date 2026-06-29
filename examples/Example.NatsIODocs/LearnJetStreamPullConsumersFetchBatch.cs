using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamPullConsumersFetchBatch(NatsServerFixture fixture, ITestOutputHelper output)
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

        // Fetch a batch of up to 10 orders, waiting up to 2 seconds for them. The
        // call returns the messages it has when the batch is full or the wait
        // elapses. Process and ack each, then fetch again to keep going.
        await foreach (var msg in consumer.FetchAsync<string>(
                           opts: new NatsJSFetchOpts { MaxMsgs = 10, Expires = TimeSpan.FromSeconds(2) }))
        {
            output.WriteLine($"shipping {msg.Data}");
            await msg.AckAsync();
            shipped++;
        }

        // NATS-DOC-END
        Assert.Equal(2, shipped);
    }
}
