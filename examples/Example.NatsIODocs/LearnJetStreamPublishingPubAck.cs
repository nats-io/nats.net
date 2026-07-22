using System.Globalization;
using NATS.Client.Core;
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
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });
        var js = client.CreateJetStreamContext();

        // The ORDERS stream captures every subject under `orders.`
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // NATS-DOC-START
        // Publish an order and inspect the ack the stream returns
        var ack = await js.PublishAsync<Order>(
            subject: "orders.created",
            data: new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture)));

        // The ack tells you which stream stored the message, at what
        // sequence, and whether it was deduplicated
        output.WriteLine($"Stream:    {ack.Stream}");
        output.WriteLine($"Sequence:  {ack.Seq}");
        output.WriteLine($"Duplicate: {ack.Duplicate}");

        // NATS-DOC-END
    }
}
