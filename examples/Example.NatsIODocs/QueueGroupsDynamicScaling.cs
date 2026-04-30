using NATS.Net;

// NATS-DOC-START
internal static class QueueGroupsDynamicScaling
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        const string subject = "tasks";
        const string queueName = "workers";

        var workers = new List<(int Id, CancellationTokenSource Cts, Task Task)>();

        // Scale up
        for (var i = 1; i <= 5; i++)
            workers.Add(StartWorker(i));

        // ... do some work, then scale down

        // Scale down
        foreach (var (_, cts, _) in workers)
            await cts.CancelAsync();

        await Task.WhenAll(workers.Select(w => w.Task));

        (int, CancellationTokenSource, Task) StartWorker(int id)
        {
            var cts = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                try
                {
                    await foreach (var msg in client.SubscribeAsync<string>(subject, queueGroup: queueName, cancellationToken: cts.Token))
                    {
                        Console.WriteLine($"Worker {id} processing: {msg.Data}");
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
            return (id, cts, task);
        }
    }
}

// NATS-DOC-END
