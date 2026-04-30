using NATS.Net;

internal static class RequestReplyTimeout
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        using var serviceCts = new CancellationTokenSource();

        // Slow service: receives the request but delays longer than the caller's timeout
        var service = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("service", cancellationToken: serviceCts.Token))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), serviceCts.Token);
                    await msg.ReplyAsync("late reply", cancellationToken: serviceCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        await client.PingAsync();

        // NATS-DOC-START
        // Cancellation token sets the timeout for the request
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var reply = await client.RequestAsync<string>("service", cancellationToken: cts.Token);
            Console.WriteLine($"Response: {reply.Data}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("No Response: timed out");
        }

        // NATS-DOC-END
        await serviceCts.CancelAsync();
        await service;
    }
}
