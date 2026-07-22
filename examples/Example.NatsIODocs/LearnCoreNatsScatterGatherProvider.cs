using System.Globalization;
using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsScatterGatherProvider(NatsServerFixture fixture, ITestOutputHelper output)
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
            // A shipping-quote provider. Subscribe plainly to shipping.quote (NOT in a
            // queue group, so every provider sees each request) and reply with a price.
            // Run several copies, each quoting a different number.
            await foreach (var msg in client.SubscribeAsync<Order>("shipping.quote"))
            {
                await msg.ReplyAsync(new ShippingQuote(Carrier: "carrier-a", QuoteCents: 1500));
            }

            // NATS-DOC-END
        });

        // Give the subscription task time to start before publishing
        await Task.Delay(1000);
        var order = new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture));
        var reply = await client.RequestAsync<Order, ShippingQuote>("shipping.quote", order);
        output.WriteLine($"quote: {reply.Data}");
    }
}
