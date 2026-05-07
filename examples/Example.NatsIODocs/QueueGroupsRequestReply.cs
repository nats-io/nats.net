using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class QueueGroupsRequestReply(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Start three service instances sharing the same queue group
        for (var i = 1; i <= 3; i++)
        {
            var id = i;
            _ = Task.Run(async () =>
            {
                await foreach (var msg in client.SubscribeAsync<string>("api.calculate", queueGroup: "api-workers"))
                {
                    await msg.ReplyAsync($"Result from instance {id}");
                }
            });
        }

        // Let subscription tasks start
        await Task.Delay(1000);

        // Make 10 requests; the queue group balances them across instances
        var replyOpts = new NatsSubOpts { Timeout = TimeSpan.FromSeconds(1) };
        for (var i = 0; i < 10; i++)
        {
            var reply = await client.RequestAsync<string>("api.calculate", replyOpts: replyOpts);
            output.WriteLine($"{i}) {reply.Data}");
        }

        // NATS-DOC-END
    }
}
