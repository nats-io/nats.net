using NATS.Net;

internal static class RequestReplyMultipleResponders
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Set up 2 instances of the service (no queue group, so both reply to each request)
        var service1 = Service(1);
        var service2 = Service(2);

        await client.PingAsync(cts.Token);

        // The first reply wins; later replies are dropped
        try
        {
            using var reqCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var reply = await client.RequestAsync<string>("service", cancellationToken: reqCts.Token);
            Console.WriteLine(reply.Data);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("No Response");
        }

        // NATS-DOC-END
        await cts.CancelAsync();
        await Task.WhenAll(service1, service2);

        async Task Service(int id)
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("service", cancellationToken: cts.Token))
                {
                    await msg.ReplyAsync($"Result from service instance {id}", cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
