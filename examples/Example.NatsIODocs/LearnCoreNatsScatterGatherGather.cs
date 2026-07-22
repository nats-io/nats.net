using System.Globalization;
using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsScatterGatherGather(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });

        // Three shipping-quote providers, each answering on shipping.quote.
        for (var i = 0; i < 3; i++)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var msg in client.SubscribeAsync<Order>("shipping.quote"))
                {
                    await msg.ReplyAsync(new ShippingQuote(Carrier: "carrier-a", QuoteCents: 1500));
                }
            });
        }

        // Give the subscription tasks time to start before publishing
        await Task.Delay(1000);

        // NATS-DOC-START
        // Scatter one request to every shipping-quote provider and gather the
        // replies. Subscribe to a private inbox, publish the request with that inbox
        // as the reply subject, then collect quotes until they stop arriving and
        // pick the cheapest.
        var order = new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200, Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture));
        var inbox = client.Connection.NewInbox();
        await using var sub = await client.Connection.SubscribeCoreAsync<ShippingQuote>(inbox);
        await client.PublishAsync<Order>("shipping.quote", order, replyTo: inbox);

        var quotes = new List<ShippingQuote>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
            {
                quotes.Add(msg.Data!);
            }
        }
        catch (OperationCanceledException)
        {
        }

        output.WriteLine($"gathered {quotes.Count} quotes");

        // NATS-DOC-END
    }
}
