using NATS.Net;

[Collection("nats-server")]
public class SubjectsSingleWildcard(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Subscribe to the shipped orders
        var shipped = SubscribeAsync(client, "orders.*.shipped", cts.Token);

        var placed = SubscribeAsync(client, "orders.*.placed", cts.Token);

        // Subscribe to the retail orders
        var retail = SubscribeAsync(client, "orders.retail.*", cts.Token);

        // Allow subscriptions to register before publishing
        await client.PingAsync(cts.Token);

        // Publish messages to the various subjects
        await client.PublishAsync("orders.wholesale.placed", "Order W73737");
        await client.PublishAsync("orders.retail.placed", "Order R65432");
        await client.PublishAsync("orders.wholesale.shipped", "Order W73001");
        await client.PublishAsync("orders.retail.shipped", "Order R65321");

        await Task.WhenAll(shipped, placed, retail);

        // NATS-DOC-END
    }

    private static async Task SubscribeAsync(NatsClient client, string filter, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in client.SubscribeAsync<string>(filter, cancellationToken: cancellationToken))
            {
                Console.WriteLine($"[{filter,-20}] {msg.Data,-12} ({msg.Subject})");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
