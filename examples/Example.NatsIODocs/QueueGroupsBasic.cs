using NATS.Net;

internal static class QueueGroupsBasic
{
    public static async Task RunAsync()
    {
        // NATS-DOC-START
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var count1 = 0;
        var count2 = 0;
        var count3 = 0;

        var worker1 = Worker("Subscriber 1", () => Interlocked.Increment(ref count1));
        var worker2 = Worker("Subscriber 2", () => Interlocked.Increment(ref count2));
        var worker3 = Worker("Subscriber 3", () => Interlocked.Increment(ref count3));

        // Ensure subscriptions are at the server before publishing
        await client.PingAsync(cts.Token);

        for (var i = 1; i <= 10; i++)
        {
            await client.PublishAsync("orders.new", $"Order Number: {i}");
        }

        Console.WriteLine("Messages published to orders.new");

        await Task.WhenAll(worker1, worker2, worker3);

        Console.WriteLine($"Subscriber 1 received {count1} messages.");
        Console.WriteLine($"Subscriber 2 received {count2} messages.");
        Console.WriteLine($"Subscriber 3 received {count3} messages.");

        async Task Worker(string name, Action onMessage)
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("orders.new", queueGroup: "new-orders-queue", cancellationToken: cts.Token))
                {
                    onMessage();
                    Console.WriteLine($"{name} Received: {msg.Data}");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        // NATS-DOC-END
    }
}
