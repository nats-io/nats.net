using NATS.Net;

public class QueueGroupsBasic
{
    [Fact(Skip = "Shows client initialization with the default port; not run in CI.")]
    public async Task RunAsync()
    {
        // NATS-DOC-START
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var countA = 0;
        var countB = 0;
        var countC = 0;

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

        // Set up the subscribers
        var workerA = Worker("Subscriber A", () => Interlocked.Increment(ref countA));
        var workerB = Worker("Subscriber B", () => Interlocked.Increment(ref countB));
        var workerC = Worker("Subscriber C", () => Interlocked.Increment(ref countC));

        // Ensure subscriptions are at the server before publishing
        await client.PingAsync(cts.Token);

        // Publish messages once all subscriptions are set up
        for (var i = 1; i <= 10; i++)
        {
            await client.PublishAsync("orders.new", $"Order Number: {i}");
        }

        Console.WriteLine("Messages published to orders.new");

        await Task.WhenAll(workerA, workerB, workerC);

        Console.WriteLine($"Subscriber A received {countA} messages.");
        Console.WriteLine($"Subscriber B received {countB} messages.");
        Console.WriteLine($"Subscriber C received {countC} messages.");

        // NATS-DOC-END
    }
}
