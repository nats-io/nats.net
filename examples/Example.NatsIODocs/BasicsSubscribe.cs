using NATS.Net;

internal static class BasicsSubscribe
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        // NATS-DOC-START
        // Subscribe to 'weather.updates' and process messages
        await foreach (var msg in client.SubscribeAsync<string>("weather.updates"))
        {
            Console.WriteLine($"Received: {msg.Data}");
        }

        // NATS-DOC-END
    }
}
