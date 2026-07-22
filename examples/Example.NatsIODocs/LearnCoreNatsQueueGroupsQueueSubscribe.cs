using System.Globalization;
using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsQueueGroupsQueueSubscribe(NatsServerFixture fixture, ITestOutputHelper output)
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
            // Join the "packers" queue group on orders.created. Every subscriber that
            // names the same group shares the load: each order is delivered to exactly
            // one member. Run this in several processes to watch the load balance.
            await foreach (var msg in client.SubscribeAsync<Order>("orders.created", queueGroup: "packers"))
            {
                output.WriteLine($"packer handling: {msg.Data}");
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
        await client.PublishAsync<Order>("orders.created", order);

        // Give the subscriber time to receive before the client is disposed
        await Task.Delay(500);
    }
}
