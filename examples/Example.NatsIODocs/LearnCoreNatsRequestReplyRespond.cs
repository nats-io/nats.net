using System.Globalization;
using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsRequestReplyRespond(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });
        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // The inventory service: subscribe to orders.inventory.check and answer
            // every request by replying on the subject each one carries.
            await foreach (var msg in client.SubscribeAsync<Order>("orders.inventory.check"))
            {
                await msg.ReplyAsync(new InventoryReply(InStock: true, Warehouse: "us-east"));
            }

            // NATS-DOC-END
        });

        // Give the subscription task time to start before publishing
        await Task.Delay(1000);
        var order = new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture));
        var reply = await client.RequestAsync<Order, InventoryReply>("orders.inventory.check", order);
        output.WriteLine($"inventory replied: {reply.Data}");
    }
}
