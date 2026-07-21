using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamMessageTtlPublishWithTtl(NatsServerFixture fixture, ITestOutputHelper output)
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

        // Per-message TTLs only work when the stream opts in with AllowMsgTTL.
        // Without it the server ignores the `Nats-TTL` header on publish.
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"])
        {
            AllowMsgTTL = true,
        });

        // NATS-DOC-START
        // Give one message its own lifetime with the `Nats-TTL` header. The
        // server deletes this message 60 seconds after it's stored, while the
        // rest of the stream keeps its normal retention.
        var headers = new NatsHeaders { ["Nats-TTL"] = "60s" };

        var ack = await js.PublishAsync(
            subject: "orders.cancelled",
            data: """{"order_id":"ord_8w2k","reason":"customer_request"}""",
            headers: headers);

        output.WriteLine($"Stored in {ack.Stream} at sequence {ack.Seq}");

        // NATS-DOC-END
        Assert.NotNull(ack);
    }
}
