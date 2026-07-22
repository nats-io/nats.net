using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamShapingTheStreamDiscardNew(NatsServerFixture fixture, ITestOutputHelper output)
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

        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // A couple of orders so the capped stream is already over its limit
        await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_8w2k"));
        await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_2zr9"));

        var rejected = false;

        // NATS-DOC-START
        // Switch ORDERS to Discard New. Discard New never drops stored messages,
        // so capping it at one message leaves the existing orders in place and
        // puts the stream instantly over its limit; the next publish is rejected.
        var stream = await js.GetStreamAsync("ORDERS");
        var config = stream.Info.Config;
        config.Discard = StreamConfigDiscard.New;
        config.MaxMsgs = 1;
        await js.UpdateStreamAsync(config);

        // This publish hits the full stream and is rejected instead of
        // succeeding silently. Handle it in the publisher.
        try
        {
            var ack = await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_5k1m"));

            // PublishAsync returns the server's answer; EnsureSuccess turns a
            // rejection into an exception instead of leaving it unchecked.
            ack.EnsureSuccess();
        }
        catch (NatsJSApiException e)
        {
            output.WriteLine($"publish rejected: {e.Message}");
            rejected = true;
        }

        // Put ORDERS back: Discard Old, no message cap (age and byte limits stay).
        config.Discard = StreamConfigDiscard.Old;
        config.MaxMsgs = -1;
        await js.UpdateStreamAsync(config);

        // NATS-DOC-END
        Assert.True(rejected);
    }
}
