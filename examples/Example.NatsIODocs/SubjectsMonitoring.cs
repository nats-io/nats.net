using NATS.Net;

internal static class SubjectsMonitoring
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Subscribe to everything
        var subscribe = Task.Run(async () =>
        {
            var received = 0;
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>(">", cancellationToken: cts.Token))
                {
                    Console.WriteLine($"[MONITOR] {msg.Subject} --> {msg.Data}");
                    if (++received == 3)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        // NATS-DOC-END
        await client.PingAsync(cts.Token);

        await client.PublishAsync("hello", "Hello NATS!");
        await client.PublishAsync("event.new", "click");
        await client.PublishAsync("weather.north.fr", "Temperature: 11C");

        Console.WriteLine("Waiting for messages...");
        await subscribe;
    }
}
