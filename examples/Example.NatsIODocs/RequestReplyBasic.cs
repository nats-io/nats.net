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
        // DateTime would be serialized as an ISO-formatted string, just like all primitive types.
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<DateTime>("time"))
            {
                await msg.ReplyAsync(DateTime.Now);
            }
        });

        // Let the subscription task start
        await Task.Delay(1000);

        // Make a request
        var reply = await client.RequestAsync<DateTime>("time");
        output.WriteLine($"Time is {reply.Data:O}");

        // NATS-DOC-END
    }
}
