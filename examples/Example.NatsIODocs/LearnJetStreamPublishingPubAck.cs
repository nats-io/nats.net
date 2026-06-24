using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamPublishingPubAck(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // The ORDERS stream captures every subject under `orders.`
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // NATS-DOC-START
        // Publish an order and inspect the ack the stream returns
        var ack = await js.PublishAsync(
            subject: "orders.created",
            data: """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""");

        // The ack tells you which stream stored the message, at what
        // sequence, and whether it was deduplicated
        output.WriteLine($"Stream:    {ack.Stream}");
        output.WriteLine($"Sequence:  {ack.Seq}");
        output.WriteLine($"Duplicate: {ack.Duplicate}");

        // NATS-DOC-END
    }
}
