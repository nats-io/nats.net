using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsPublishSubscribeSubscribe(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // Subscribe as the warehouse service to orders.created. Each matching
            // message is delivered to this subscription as it is published.
            await foreach (var msg in client.SubscribeAsync<string>("orders.created"))
            {
                output.WriteLine($"warehouse received: {msg.Data}");
            }

            // NATS-DOC-END
        });

        await Task.Delay(1000);
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        await client.PublishAsync("orders.created", order);
        await Task.Delay(500);
    }
}
