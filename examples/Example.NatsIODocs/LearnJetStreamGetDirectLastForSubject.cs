using System.Text;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamGetDirectLastForSubject(NatsServerFixture fixture, ITestOutputHelper output)
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

        // Seed a few orders so `orders.shipped` has more than one message
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");
        await js.PublishAsync(subject: "orders.shipped", data: """{"order_id":"ord_8w2k","customer":"acme-co"}""");
        await js.PublishAsync(subject: "orders.created", data: """{"order_id":"ord_2zr9","customer":"globex"}""");
        await js.PublishAsync(subject: "orders.shipped", data: """{"order_id":"ord_2zr9","customer":"globex"}""");

        var stream = await js.GetStreamAsync("ORDERS");

        // NATS-DOC-START
        // Fetch the most recent message on a subject. This is a regular get,
        // served by the stream leader.
        var response = await stream.GetAsync(new StreamMsgGetRequest { LastBySubj = "orders.shipped" });

        var message = response.Message;
        var payload = Encoding.UTF8.GetString(message.Data.Span);
        output.WriteLine($"Subject: {message.Subject}");
        output.WriteLine($"Payload: {payload}");

        // NATS-DOC-END
        Assert.Equal("orders.shipped", message.Subject);
    }
}
