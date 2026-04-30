using NATS.Client.Core;
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
        // Set the per-request timeout via reply options
        var replyOpts = new NatsSubOpts { Timeout = TimeSpan.FromSeconds(1) };

        try
        {
            var reply = await client.RequestAsync<string>("service", replyOpts: replyOpts);
            Console.WriteLine($"Response: {reply.Data}");
        }
        catch (NatsNoReplyException)
        {
            Console.WriteLine("No Response: timed out");
        }

        // NATS-DOC-END
        await serviceCts.CancelAsync();
        await service;
    }
}
