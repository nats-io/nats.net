using NATS.Net;

internal static class BasicsSubscribe
{
    public static async Task RunAsync()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await using var client = new NatsClient();

        // NATS-DOC-START
        // Subscribe to 'weather.updates' and process messages
        await foreach (var msg in client.SubscribeAsync<string>("weather.updates", cancellationToken: cts.Token))
        {
            Console.WriteLine($"Received: {msg.Data}");
        }

        // NATS-DOC-END
    }
}
