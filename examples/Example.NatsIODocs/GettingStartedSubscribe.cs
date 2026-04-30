using NATS.Net;

// NATS-DOC-START
internal static class GettingStartedSubscribe
{
    public static async Task RunAsync()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await using var client = new NatsClient("demo.nats.io");

        Console.WriteLine("Waiting for messages on 'hello'...");

        // Subscribe to 'hello' and process messages
        await foreach (var msg in client.SubscribeAsync<string>("hello", cancellationToken: cts.Token))
        {
            Console.WriteLine($"Received: {msg.Data}");
        }
    }
}

// NATS-DOC-END
