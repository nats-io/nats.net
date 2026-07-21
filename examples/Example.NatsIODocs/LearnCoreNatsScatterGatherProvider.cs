using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsScatterGatherProvider(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // A shipping-quote provider. Subscribe plainly to shipping.quote (NOT in a
            // queue group, so every provider sees each request) and reply with a price.
            // Run several copies, each quoting a different number.
            await foreach (var msg in client.SubscribeAsync<string>("shipping.quote"))
            {
                await msg.ReplyAsync("""{"carrier":"carrier-a","quote_cents":1500}""");
            }

            // NATS-DOC-END
        });

        await Task.Delay(1000);
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        var reply = await client.RequestAsync<string, string>("shipping.quote", order);
        output.WriteLine($"quote: {reply.Data}");
    }
}
