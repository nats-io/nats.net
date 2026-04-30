using NATS.Net;

internal static class RequestReplyCalculator
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Set up the calculator service
        var service = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("calc.add", cancellationToken: cts.Token))
                {
                    var parts = msg.Data!.Split(' ');
                    var x = int.Parse(parts[0]);
                    var y = int.Parse(parts[1]);
                    await msg.ReplyAsync((x + y).ToString(), cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        await client.PingAsync(cts.Token);

        try
        {
            using var reqCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var reply = await client.RequestAsync<string, string>("calc.add", "5 3", cancellationToken: reqCts.Token);
            Console.WriteLine($"5 + 3 = {reply.Data}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("No Response");
        }

        try
        {
            using var reqCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var reply = await client.RequestAsync<string, string>("calc.add", "10 7", cancellationToken: reqCts.Token);
            Console.WriteLine($"10 + 7 = {reply.Data}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("No Response");
        }

        // NATS-DOC-END
        await cts.CancelAsync();
        await service;
    }
}
