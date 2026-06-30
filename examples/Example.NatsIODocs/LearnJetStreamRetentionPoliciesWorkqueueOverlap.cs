using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamRetentionPoliciesWorkqueueOverlap(NatsServerFixture fixture, ITestOutputHelper output)
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

        // FULFILLMENT is the WorkQueue stream of orders waiting to ship
        await js.CreateStreamAsync(new StreamConfig(name: "FULFILLMENT", subjects: ["fulfill.>"])
        {
            Retention = StreamConfigRetention.Workqueue,
        });

        // NATS-DOC-START
        // One unfiltered consumer over the whole queue is fine.
        await js.CreateOrUpdateConsumerAsync("FULFILLMENT", new ConsumerConfig("shippers")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
        });

        // A second unfiltered consumer would see the same orders, so two workers
        // could each be handed the same task. A WorkQueue stream refuses it.
        try
        {
            await js.CreateOrUpdateConsumerAsync("FULFILLMENT", new ConsumerConfig("eu-shippers")
            {
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
            });
        }
        catch (NatsJSApiException e)
        {
            output.WriteLine($"rejected (err {e.Error.ErrCode}): {e.Error.Description}");
        }

        // Give each worker its own slice of the subjects instead. Drop the
        // catch-all consumer first...
        await js.DeleteConsumerAsync("FULFILLMENT", "shippers");

        // ...then create two consumers whose filters don't overlap. Both succeed,
        // because no order can be claimed by more than one of them.
        await js.CreateOrUpdateConsumerAsync("FULFILLMENT", new ConsumerConfig("us-shippers")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = "fulfill.us",
        });

        await js.CreateOrUpdateConsumerAsync("FULFILLMENT", new ConsumerConfig("eu-shippers")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = "fulfill.eu",
        });

        output.WriteLine("Created us-shippers and eu-shippers on non-overlapping filters");

        // NATS-DOC-END
        var us = await js.GetConsumerAsync("FULFILLMENT", "us-shippers");
        var eu = await js.GetConsumerAsync("FULFILLMENT", "eu-shippers");
        Assert.Equal("fulfill.us", us.Info.Config.FilterSubject);
        Assert.Equal("fulfill.eu", eu.Info.Config.FilterSubject);
    }
}
