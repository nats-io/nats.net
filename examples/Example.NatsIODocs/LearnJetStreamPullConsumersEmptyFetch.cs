using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamPullConsumersEmptyFetch(NatsServerFixture fixture, ITestOutputHelper output)
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

        // A stream and consumer with no orders waiting
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));
        await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("shipping")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
        });

        var shipped = 0;

        // NATS-DOC-START
        // Bind to the durable "shipping" consumer.
        var consumer = await js.GetConsumerAsync("ORDERS", "shipping");

        // On a drained consumer the fetch ends with no messages once the expiry
        // passes, not with an error. Treat "nothing right now" as normal: if no
        // orders came back, wait and fetch again instead of failing.
        await foreach (var msg in consumer.FetchAsync<string>(
                           opts: new NatsJSFetchOpts { MaxMsgs = 10, Expires = TimeSpan.FromSeconds(2) }))
        {
            output.WriteLine($"shipping {msg.Data}");
            await msg.AckAsync();
            shipped++;
        }

        if (shipped == 0)
        {
            output.WriteLine("no orders waiting, will retry");
        }

        // NATS-DOC-END
        Assert.Equal(0, shipped);
    }
}
