using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamPublishingSync(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // The ORDERS stream captures every subject under `orders.`
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // NATS-DOC-START
        // Publish each order and read the ack the stream returns
        var ack1 = await js.PublishAsync(
            subject: "orders.created",
            data: """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""");
        output.WriteLine($"Stored in {ack1.Stream} at sequence {ack1.Seq}");

        var ack2 = await js.PublishAsync(
            subject: "orders.created",
            data: """{"order_id":"ord_2zr9","customer":"globex","total_cents":7800,"ts":"2026-05-22T10:14:25Z"}""");
        output.WriteLine($"Stored in {ack2.Stream} at sequence {ack2.Seq}");

        var ack3 = await js.PublishAsync(
            subject: "orders.shipped",
            data: """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:31Z"}""");
        output.WriteLine($"Stored in {ack3.Stream} at sequence {ack3.Seq}");

        // NATS-DOC-END
    }
}
