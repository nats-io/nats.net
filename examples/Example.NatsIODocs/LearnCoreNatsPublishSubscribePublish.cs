using System.Globalization;
using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsPublishSubscribePublish(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });
        var sub = await client.Connection.SubscribeCoreAsync<Order>("orders.created");

        // NATS-DOC-START
        // Publish one order to the orders.created subject. Publishing is
        // fire-and-forget: the call hands the message to the server and returns.
        // The client serializes the Order record to JSON by default.
        var order = new Order(
            OrderId: "ord_8w2k",
            Customer: "acme-co",
            TotalCents: 4200,
            Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture));
        await client.PublishAsync<Order>("orders.created", order);

        // NATS-DOC-END
        var msg = await sub.Msgs.ReadAsync();
        output.WriteLine($"warehouse received: {msg.Data}");
    }
}
