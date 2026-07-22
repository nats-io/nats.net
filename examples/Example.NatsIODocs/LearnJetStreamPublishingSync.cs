using System.Globalization;
using NATS.Client.Core;
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
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });
        var js = client.CreateJetStreamContext();

        // The ORDERS stream captures every subject under `orders.`
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // NATS-DOC-START
        // Publish each order and read the ack the stream returns
        var ack1 = await js.PublishAsync<Order>(
            subject: "orders.created",
            data: new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture)));
        output.WriteLine($"Stored in {ack1.Stream} at sequence {ack1.Seq}");

        var ack2 = await js.PublishAsync<Order>(
            subject: "orders.created",
            data: new Order(OrderId: "ord_2zr9", Customer: "globex", TotalCents: 7800, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:25Z", CultureInfo.InvariantCulture)));
        output.WriteLine($"Stored in {ack2.Stream} at sequence {ack2.Seq}");

        var ack3 = await js.PublishAsync<Order>(
            subject: "orders.shipped",
            data: new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:31Z", CultureInfo.InvariantCulture)));
        output.WriteLine($"Stored in {ack3.Stream} at sequence {ack3.Seq}");

        // NATS-DOC-END
    }
}
