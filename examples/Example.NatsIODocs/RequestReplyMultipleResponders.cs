using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class RequestReplyMultipleResponders(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Two service instances reply on the same subject; the first reply wins
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("calc.add"))
            {
                await msg.ReplyAsync("calculated result from A");
            }
        });

        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("calc.add"))
            {
                await msg.ReplyAsync("calculated result from B");
            }
        });

        // Let the subscriptions register
        await Task.Delay(1000);

        var reply = await client.RequestAsync<string>("calc.add");
        output.WriteLine($"Got response: {reply.Data}");

        // NATS-DOC-END
    }
}
