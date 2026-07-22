using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamGetDirectDirectRead(NatsServerFixture fixture, ITestOutputHelper output)
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

        // Direct get must be enabled on the stream (AllowDirect) before
        // GetDirectAsync will work
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]) { AllowDirect = true });

        // Seed a few orders so there's something at each stream sequence
        await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_8w2k", Customer: "acme-co"));
        await js.PublishAsync<Order>(subject: "orders.shipped", data: new Order(OrderId: "ord_8w2k", Customer: "acme-co"));
        await js.PublishAsync<Order>(subject: "orders.created", data: new Order(OrderId: "ord_2zr9", Customer: "globex"));
        await js.PublishAsync<Order>(subject: "orders.shipped", data: new Order(OrderId: "ord_2zr9", Customer: "globex"));

        var stream = await js.GetStreamAsync("ORDERS");

        // NATS-DOC-START
        // Read sequence 1 with a direct get. Any replica can serve this, not
        // just the stream leader. The original subject comes back as a header.
        var msg = await stream.GetDirectAsync<Order>(new StreamMsgGetRequest { Seq = 1 });

        msg.Headers!.TryGetLastValue("Nats-Subject", out var subject);
        output.WriteLine($"Subject: {subject}");
        output.WriteLine($"Payload: {msg.Data}");

        // NATS-DOC-END
        Assert.Equal("orders.created", subject);
    }
}
