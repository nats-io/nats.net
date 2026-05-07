using NATS.Net;

internal static class SubjectsMultiWildcard
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Subscribe to all non-critical alarms
        var alarm = SubscribeAsync(client, "sensor.alarm.*", cts.Token);

        // Subscribe to all critical
        var critical = SubscribeAsync(client, "sensor.*.*.critical", cts.Token);

        // Subscribe to everything
        var all = SubscribeAsync(client, "sensor.>", cts.Token);

        // Allow subscriptions to register before publishing
        await client.PingAsync(cts.Token);

        // Publish to specific subjects
        await client.PublishAsync("sensor.alarm.smoke", "kitchen,14:22");
        await client.PublishAsync("sensor.alarm.smoke.critical", "kitchen,14:23");
        await client.PublishAsync("sensor.alarm.water", "basement,16:42");
        await client.PublishAsync("sensor.alarm.water.critical", "basement,16:43");

        await Task.WhenAll(alarm, critical, all);

        // NATS-DOC-END
    }

    private static async Task SubscribeAsync(NatsClient client, string filter, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in client.SubscribeAsync<string>(filter, cancellationToken: cancellationToken))
            {
                Console.WriteLine($"[{filter,-20}] {msg.Data,-15} ({msg.Subject})");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
