using NATS.Net;

internal static class SubjectsSingleWildcard
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var shipped = SubscribeAsync(client, "orders.*.shipped", cts.Token);
        var placed = SubscribeAsync(client, "orders.*.placed", cts.Token);
        var retail = SubscribeAsync(client, "orders.retail.*", cts.Token);

        // Allow subscriptions to register before publishing
        await client.PingAsync(cts.Token);

        await client.PublishAsync("orders.wholesale.placed", "Order W73737");
        await client.PublishAsync("orders.retail.placed", "Order R65432");
        await client.PublishAsync("orders.wholesale.shipped", "Order W73001");
        await client.PublishAsync("orders.retail.shipped", "Order R65321");

        await Task.WhenAll(shipped, placed, retail);
    }

    private static async Task SubscribeAsync(NatsClient client, string filter, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in client.SubscribeAsync<string>(filter, cancellationToken: cancellationToken))
            {
                var parts = msg.Subject.Split('.');
                Console.WriteLine($"[{filter}] {msg.Data}: {parts[1]},{parts[2]}");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
