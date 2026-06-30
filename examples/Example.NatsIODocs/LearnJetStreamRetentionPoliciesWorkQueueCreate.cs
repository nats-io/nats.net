using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamRetentionPoliciesWorkQueueCreate(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Start from a clean stream (the test server is shared across the collection)
        try
        {
            await js.DeleteStreamAsync("FULFILLMENT");
        }
        catch (NatsJSApiException)
        {
            // Stream doesn't exist yet, nothing to delete
        }

        // NATS-DOC-START
        // FULFILLMENT is the queue of paid orders waiting to ship. WorkQueue
        // retention means an order leaves the stream the moment a worker acks it.
        var stream = await js.CreateStreamAsync(new StreamConfig(name: "FULFILLMENT", subjects: ["fulfill.>"])
        {
            Retention = StreamConfigRetention.Workqueue,
        });

        output.WriteLine($"FULFILLMENT retention is {stream.Info.Config.Retention}");

        // One paid order waiting to ship to a US address
        await js.PublishAsync(subject: "fulfill.us", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");

        // A durable pull consumer the shipping workers share
        var consumer = await js.CreateOrUpdateConsumerAsync("FULFILLMENT", new ConsumerConfig("shippers")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
        });

        // Fetch the one order and ack it. DoubleAck waits for the server to confirm
        // the ack, so the WorkQueue removal has happened before we read the count.
        await foreach (var msg in consumer.FetchAsync<string>(opts: new NatsJSFetchOpts { MaxMsgs = 1 }))
        {
            output.WriteLine($"shipping {msg.Data}");
            await msg.AckAsync(new AckOpts { DoubleAck = true });
        }

        // The ack removed the task, so a WorkQueue stream drains back to empty. A
        // Limits stream like ORDERS would still be holding this order.
        var info = await js.GetStreamAsync("FULFILLMENT");
        output.WriteLine($"Messages left in FULFILLMENT: {info.Info.State.Messages}");

        // NATS-DOC-END
        Assert.Equal(StreamConfigRetention.Workqueue, stream.Info.Config.Retention);
        Assert.Equal(0L, info.Info.State.Messages);
    }
}
