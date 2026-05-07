using NATS.Net;

[Collection("nats-server")]
public class RequestReplyBasic(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Set up the time service
        var service = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("time", cancellationToken: cts.Token))
                {
                    await msg.ReplyAsync(DateTimeOffset.UtcNow.ToString("O"), cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        await client.PingAsync(cts.Token);

        // Make a request with a per-call timeout
        try
        {
            using var reqCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var reply = await client.RequestAsync<string>("time", cancellationToken: reqCts.Token);
            Console.WriteLine($"Time is {reply.Data}");
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
