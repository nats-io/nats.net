using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsRequestReplyReplies(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // Two inventory instances that both answer.
        for (var i = 0; i < 2; i++)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var msg in client.SubscribeAsync<string>("orders.inventory.check"))
                {
                    await msg.ReplyAsync("""{"in_stock":true,"warehouse":"us-east"}""");
                }
            });
        }

        await Task.Delay(1000);

        // NATS-DOC-START
        // Gather more than one reply to a single request. A plain request returns
        // only the first reply, so when several services may answer, subscribe to
        // your own inbox, publish the request with that inbox as the reply subject,
        // and collect replies until they stop arriving.
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        var inbox = client.Connection.NewInbox();
        await using var sub = await client.Connection.SubscribeCoreAsync<string>(inbox);
        await client.PublishAsync("orders.inventory.check", order, replyTo: inbox);

        var replies = new List<string>();
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
