using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class RequestReplyBasic(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Set up a service that replies with the current time
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("time"))
            {
                await msg.ReplyAsync(DateTimeOffset.UtcNow.ToString("O"));
            }
        });

        // Let the subscription register
        await Task.Delay(1000);

        // Make a request
        var reply = await client.RequestAsync<string>("time");
        output.WriteLine($"Time is {reply.Data}");

        // NATS-DOC-END
    }
}
