using NATS.Net;

[Collection("nats-server")]
public class QueueGroupsMixedSubscribers(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Audit logger receives all order messages
        var audit = Subscriber("[AUDIT]", "orders.>", queueGroup: null);
        var metrics = Subscriber("[METRICS]", "orders.>", queueGroup: null);

        // Workers share the load via a queue group
        var workerA = Subscriber("[WORKER A]", "orders.new", queueGroup: "new-orders-queue");
        var workerB = Subscriber("[WORKER B]", "orders.new", queueGroup: "new-orders-queue");

        await client.PingAsync(cts.Token);

        // Audit and metrics see every message; one worker processes each
        await client.PublishAsync("orders.new", "Order 123");
        await client.PublishAsync("orders.new", "Order 124");

        // NATS-DOC-END
        await Task.WhenAll(audit, metrics, workerA, workerB);

        async Task Subscriber(string label, string subject, string? queueGroup)
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>(subject, queueGroup: queueGroup, cancellationToken: cts.Token))
                {
                    Console.WriteLine($"{label} {msg.Subject}: {msg.Data}");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
