using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class RequestReplyTimeout(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // Slow service: receives the request but delays longer than the caller's timeout
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("service"))
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await msg.ReplyAsync("late reply");
            }
        });

        // Let the subscription register
        await Task.Delay(1000);

        // NATS-DOC-START
        // Set the per-request timeout via reply options
        var replyOpts = new NatsSubOpts { Timeout = TimeSpan.FromSeconds(1) };
        try
        {
            var reply = await client.RequestAsync<string>("service", replyOpts: replyOpts);
            output.WriteLine($"Response: {reply.Data}");
        }
        catch (NatsNoReplyException)
        {
            output.WriteLine("No Response: timed out");
        }

        // NATS-DOC-END
    }
}
