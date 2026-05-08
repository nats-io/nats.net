using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class QueueGroupsMixedSubscribers(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Audit logger - receives all order messages
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.>"))
            {
                output.WriteLine($"[AUDIT] {msg.Subject}: {msg.Data}");
            }
        });

        // Metrics collector - receives all order messages
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.>"))
            {
                output.WriteLine($"[METRICS] {msg.Subject}: {msg.Data}");
            }
        });

        // Workers - share load via a queue group
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.new", queueGroup: "workers"))
            {
                output.WriteLine($"[WORKER A] Processing: {msg.Data}");
            }
        });

        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.new", queueGroup: "workers"))
            {
                output.WriteLine($"[WORKER B] Processing: {msg.Data}");
            }
        });

        // Let subscription tasks start
        await Task.Delay(1000);

        // Audit and metrics see every message; one worker processes each
        await client.PublishAsync("orders.new", "Order 123");

        // NATS-DOC-END
        await Task.Delay(1000);
    }
}
