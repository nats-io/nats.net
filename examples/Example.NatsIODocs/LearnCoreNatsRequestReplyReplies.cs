using System.Globalization;
using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsRequestReplyReplies(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });

        // Two inventory instances that both answer.
        for (var i = 0; i < 2; i++)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var msg in client.SubscribeAsync<Order>("orders.inventory.check"))
                {
                    await msg.ReplyAsync(new InventoryReply(InStock: true, Warehouse: "us-east"));
                }
            });
        }

        // Give the subscription tasks time to start before publishing
        await Task.Delay(1000);

        // NATS-DOC-START
        // Gather more than one reply to a single request. A plain request returns
        // only the first reply, so when several services may answer, subscribe to
        // your own inbox, publish the request with that inbox as the reply subject,
        // and collect replies until they stop arriving.
        var order = new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture));
        var inbox = client.Connection.NewInbox();
        await using var sub = await client.Connection.SubscribeCoreAsync<InventoryReply>(inbox);
        await client.PublishAsync<Order>("orders.inventory.check", order, replyTo: inbox);

        var replies = new List<InventoryReply>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            // Stop once no further reply arrives within the gap deadline.
            await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
            {
                replies.Add(msg.Data!);
            }
        }
        catch (OperationCanceledException)
        {
        }

        output.WriteLine($"gathered {replies.Count} replies");

        // NATS-DOC-END
    }
}
