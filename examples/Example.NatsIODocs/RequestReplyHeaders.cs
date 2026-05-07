using NATS.Client.Core;
using NATS.Net;

[Collection("nats-server")]
public class RequestReplyHeaders(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Set up the header echo service
        var service = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("service", cancellationToken: cts.Token))
                {
                    var responseHeaders = new NatsHeaders
                    {
                        ["X-Response-ID"] = msg.Headers?["X-Request-ID"] ?? string.Empty,
                    };
                    if (msg.Headers != null)
                    {
                        foreach (var key in msg.Headers.Keys)
                        {
                            responseHeaders[key] = msg.Headers[key];
                        }
                    }

                    await msg.ReplyAsync(msg.Data, headers: responseHeaders, cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        await client.PingAsync(cts.Token);

        var requestHeaders = new NatsHeaders
        {
            ["X-Request-ID"] = "123",
            ["X-Priority"] = "high",
        };

        try
        {
            using var reqCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var reply = await client.RequestAsync<string, string>("service", "data", headers: requestHeaders, cancellationToken: reqCts.Token);
            Console.WriteLine($"Response: {reply.Data}");
            Console.WriteLine($"Response ID: {reply.Headers?["X-Response-ID"]}");
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
