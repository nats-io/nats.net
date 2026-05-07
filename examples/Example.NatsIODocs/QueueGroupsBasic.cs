using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class QueueGroupsBasic(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Create three workers in the same queue group
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.new", queueGroup: "new-orders-queue"))
            {
                output.WriteLine($"Worker A processed: {msg.Data}");
            }
        });

        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.new", queueGroup: "new-orders-queue"))
            {
                output.WriteLine($"Worker B processed: {msg.Data}");
            }
        });

        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.new", queueGroup: "new-orders-queue"))
            {
                output.WriteLine($"Worker C processed: {msg.Data}");
            }
        });

        // Let subscription tasks to start
        await Task.Delay(1000);

        // Publish messages once all subscriptions are set up
        for (var i = 1; i <= 10; i++)
        {
            await client.PublishAsync("orders.new", $"Order Number: {i}");
        }

        // NATS-DOC-END
        await Task.Delay(1000);
    }
}
