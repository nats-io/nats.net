using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsSubjectsAndWildcardsWildcardMulti(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // Audit service: catch every order message at any depth. The multi-token
            // wildcard > matches one or more tokens and must be the last token, so
            // orders.> matches orders.created, orders.us.created, and
            // orders.us.west.created alike.
            await foreach (var msg in client.SubscribeAsync<string>("orders.>"))
            {
                output.WriteLine($"audit: {msg.Subject}");
            }

            // NATS-DOC-END
        });

        await Task.Delay(1000);
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        await client.PublishAsync("orders.created", order);
        await client.PublishAsync("orders.shipped", order);
        await Task.Delay(500);
    }
}
