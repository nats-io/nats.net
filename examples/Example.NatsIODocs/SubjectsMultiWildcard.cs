using NATS.Net;

// NATS-DOC-START
internal static class SubjectsMultiWildcard
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Subscribe to a multi-wildcard subject
        var subscribe = Task.Run(async () =>
        {
            var received = 0;
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("weather.>", cancellationToken: cts.Token))
                {
                    Console.WriteLine($"Received weather for {msg.Subject} --> {msg.Data}");
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

        // Allow subscription to register before publishing
        await client.PingAsync(cts.Token);

        await client.PublishAsync("weather.us", "US weather update");
        await client.PublishAsync("weather.us.east", "East coast update");
        await client.PublishAsync("weather.eu.north.finland", "Finland weather");

        Console.WriteLine("Waiting for messages...");
        await subscribe;
    }
}

// NATS-DOC-END
