using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsRequestReplyRespond(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // The inventory service: subscribe to orders.inventory.check and answer
            // every request by replying on the subject each one carries.
            await foreach (var msg in client.SubscribeAsync<string>("orders.inventory.check"))
            {
                await msg.ReplyAsync("""{"in_stock":true,"warehouse":"us-east"}""");
            }

            // NATS-DOC-END
        });

        await Task.Delay(1000);
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        var reply = await client.RequestAsync<string, string>("orders.inventory.check", order);
        output.WriteLine($"inventory replied: {reply.Data}");
    }
}
