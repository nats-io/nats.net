using NATS.Net;

internal static class RequestReplyMultipleResponders
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Set up 2 instances of the service (no queue group, so both reply to each request)
        var serviceA = Service("A");
        var serviceB = Service("B");

        await client.PingAsync(cts.Token);

        // The first reply wins; later replies are dropped
        try
        {
            using var reqCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var reply = await client.RequestAsync<string>("calc.add", cancellationToken: reqCts.Token);
            Console.WriteLine($"Got response: {reply.Data}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("No Response");
        }

        // NATS-DOC-END
        await cts.CancelAsync();
        await Task.WhenAll(serviceA, serviceB);

        async Task Service(string id)
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("calc.add", cancellationToken: cts.Token))
                {
                    await msg.ReplyAsync($"calculated result from {id}", cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
