using NATS.Net;

public class QueueGroupsDynamicScaling
{
    [Fact(Skip = "Shows client initialization with the default port; not run in CI.")]
    public async Task RunAsync()
    {
        // NATS-DOC-START
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

        // NATS-DOC-END
    }
}
