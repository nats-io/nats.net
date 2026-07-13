using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsQueueGroupsQueueSubscribe(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // Join the "packers" queue group on orders.created. Every subscriber that
            // names the same group shares the load: each order is delivered to exactly
            // one member. Run this in several processes to watch the load balance.
            await foreach (var msg in client.SubscribeAsync<string>("orders.created", queueGroup: "packers"))
            {
                output.WriteLine($"packer handling: {msg.Data}");
            }

            // NATS-DOC-END
        });

        await Task.Delay(1000);
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        await client.PublishAsync("orders.created", order);
        await Task.Delay(500);
    }
}
