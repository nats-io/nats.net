using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamReadingBackRead(NatsServerFixture fixture, ITestOutputHelper output)
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

        // Publish a few orders so the consumer has something to read back
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_2zr9","customer":"globex"}""");
        await js.PublishAsync(subject: "orders.shipped", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");

        // Create the durable consumer to read from
        await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("orders-reader")
        {
            AckPolicy = ConsumerConfigAckPolicy.None,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
        });

        var read = 0;

        // NATS-DOC-START
        // Bind to the existing durable consumer
        var consumer = await js.GetConsumerAsync("ORDERS", "orders-reader");

        // Read exactly what the stream is holding, no count assumed up front
        long pending = (long)consumer.Info.NumPending;
        if (pending == 0)
        {
            output.WriteLine("nothing to read");
        }
        else
        {
            await foreach (var msg in consumer.FetchAsync<string>(opts: new NatsJSFetchOpts { MaxMsgs = (int)pending }))
            {
                output.WriteLine($"stream {msg.Metadata?.Sequence.Stream} consumer {msg.Metadata?.Sequence.Consumer}: {msg.Data}");
                read++;
            }
        }

        // Ack policy is None, so there's nothing to acknowledge
        // NATS-DOC-END
        Assert.Equal(3, read);
    }
}
