using System.Globalization;
using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsRequestReplyRequest(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });

        // A running inventory service so the request gets an answer.
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<Order>("orders.inventory.check"))
            {
                await msg.ReplyAsync(new InventoryReply(InStock: true, Warehouse: "us-east"));
            }
        });

        // Give the subscription task time to start before publishing
        await Task.Delay(1000);

        // NATS-DOC-START
        // Ask the inventory service whether an order's item is in stock. The client
        // creates a private inbox, sends the request, and waits for one reply.
        // RequestAsync throws NatsNoRespondersException immediately when nothing is
        // subscribed on the subject.
        var order = new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture));
        try
        {
            var reply = await client.RequestAsync<Order, InventoryReply>("orders.inventory.check", order);
            output.WriteLine($"inventory replied: {reply.Data}");
        }
        catch (NatsNoRespondersException)
        {
            output.WriteLine("no inventory service is running");
        }

        // NATS-DOC-END
    }
}
