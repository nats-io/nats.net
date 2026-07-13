using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsScatterGatherGather(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // Three shipping-quote providers, each answering on shipping.quote.
        for (var i = 0; i < 3; i++)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var msg in client.SubscribeAsync<string>("shipping.quote"))
                {
                    await msg.ReplyAsync("""{"carrier":"carrier-a","quote_cents":1500}""");
                }
            });
        }

        await Task.Delay(1000);

        // NATS-DOC-START
        // Scatter one request to every shipping-quote provider and gather the
        // replies. Subscribe to a private inbox, publish the request with that inbox
        // as the reply subject, then collect quotes until they stop arriving and
        // pick the cheapest.
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        var inbox = client.Connection.NewInbox();
        await using var sub = await client.Connection.SubscribeCoreAsync<string>(inbox);
        await client.PublishAsync("shipping.quote", order, replyTo: inbox);

        var quotes = new List<string>();
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
