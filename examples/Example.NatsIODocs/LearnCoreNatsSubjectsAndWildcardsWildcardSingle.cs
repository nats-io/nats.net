using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsSubjectsAndWildcardsWildcardSingle(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // Regional analytics: one subscription catches created orders from every
            // region. The single-token wildcard * matches exactly one token, so both
            // orders.us.created and orders.eu.created match, while orders.created and
            // orders.us.west.created do not.
            await foreach (var msg in client.SubscribeAsync<string>("orders.*.created"))
            {
                output.WriteLine($"analytics: new order on {msg.Subject}");
            }

            // NATS-DOC-END
        });

        await Task.Delay(1000);
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        await client.PublishAsync("orders.us.created", order);
        await client.PublishAsync("orders.created", order);
        await Task.Delay(500);
    }
}
