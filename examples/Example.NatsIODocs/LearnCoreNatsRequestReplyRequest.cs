using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsRequestReplyRequest(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // A running inventory service so the request gets an answer.
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.inventory.check"))
            {
                await msg.ReplyAsync("""{"in_stock":true,"warehouse":"us-east"}""");
            }
        });
        await Task.Delay(1000);

        // NATS-DOC-START
        // Ask the inventory service whether an order's item is in stock. The client
        // creates a private inbox, sends the request, and waits for one reply.
        // RequestAsync throws NatsNoRespondersException immediately when nothing is
        // subscribed on the subject.
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        try
        {
            var reply = await client.RequestAsync<string, string>("orders.inventory.check", order);
            output.WriteLine($"inventory replied: {reply.Data}");
        }
        catch (NatsNoRespondersException)
        {
            output.WriteLine("no inventory service is running");
        }

        // NATS-DOC-END
    }
}
