using System.Globalization;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamPublishingDedup(NatsServerFixture fixture, ITestOutputHelper output)
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
        var order = new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture));

        // Tag the message with a unique id. The stream uses it to detect duplicates.
        var opts = new NatsJSPubOpts { MsgId = "ord_8w2k-created" };

        // First publish: the stream stores the message
        var ack1 = await js.PublishAsync<Order>(subject: "orders.created", data: order, opts: opts);
        output.WriteLine($"First:  seq={ack1.Seq} duplicate={ack1.Duplicate}");

        // Republish with the same id: the stream recognizes it and stores nothing new
        var ack2 = await js.PublishAsync<Order>(subject: "orders.created", data: order, opts: opts);
        output.WriteLine($"Second: seq={ack2.Seq} duplicate={ack2.Duplicate}");

        // NATS-DOC-END
    }
}
