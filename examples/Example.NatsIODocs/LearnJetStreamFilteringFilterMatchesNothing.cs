using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamFilteringFilterMatchesNothing(NatsServerFixture fixture, ITestOutputHelper output)
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

        // The ORDERS stream already holds orders.created and orders.shipped messages
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");
        await js.PublishAsync(subject: "orders.shipped", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");

        var received = 0;

        // NATS-DOC-START
        // The filter has a typo: "orders.shiped" matches no subject the stream stores
        var consumer = await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("analytics-typo")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = "orders.shiped",
        });

        // The fetch waits out its expiry and returns nothing, with no error
        await foreach (var msg in consumer.FetchAsync<string>(opts: new NatsJSFetchOpts { MaxMsgs = 5, Expires = TimeSpan.FromSeconds(2) }))
        {
            received++;
            await msg.AckAsync();
        }

        output.WriteLine($"Pull returned {received} messages: the filter matched no stored subject, so a wrong filter fails silently");

        // NATS-DOC-END
        Assert.Equal(0, received);
    }
}
