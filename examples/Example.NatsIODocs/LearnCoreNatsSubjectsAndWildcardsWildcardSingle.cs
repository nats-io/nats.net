using System.Globalization;
using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsSubjectsAndWildcardsWildcardSingle(NatsServerFixture fixture, ITestOutputHelper output)
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
            // Regional analytics: one subscription catches created orders from every
            // region. The single-token wildcard * matches exactly one token, so both
            // orders.us.created and orders.eu.created match, while orders.created and
            // orders.us.west.created do not.
            await foreach (var msg in client.SubscribeAsync<Order>("orders.*.created"))
            {
                output.WriteLine($"analytics: new order on {msg.Subject}");
            }

            // NATS-DOC-END
        });

        // Give the subscription task time to start before publishing
        await Task.Delay(1000);
        var order = new Order(
            OrderId: "ord_8w2k",
            Customer: "acme-co",
            TotalCents: 4200,
            Timestamp: DateTimeOffset.Parse("2026-05-22T10:14:22Z", CultureInfo.InvariantCulture));
        await client.PublishAsync<Order>("orders.us.created", order);
        await client.PublishAsync<Order>("orders.created", order);

        // Give the subscriber time to receive before the client is disposed
        await Task.Delay(500);
    }
}
