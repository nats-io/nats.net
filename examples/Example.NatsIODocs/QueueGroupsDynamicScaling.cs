using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class QueueGroupsDynamicScaling(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact(Skip = "Subscriber lifecycle (unsubscribe) plumbing isn't a clean doc snippet; not run in CI.")]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Start workers in the same queue group; track them so we can scale down later
        var workers = new List<INatsSub<string>>();

        for (var i = 1; i <= 5; i++)
        {
            var id = i;
            var sub = await client.Connection.SubscribeCoreAsync<string>("tasks", queueGroup: "workers");
            _ = Task.Run(async () =>
            {
                await foreach (var msg in sub.Msgs.ReadAllAsync())
                {
                    output.WriteLine($"Worker {id} processing: {msg.Data}");
                }
            });
            workers.Add(sub);
        }

        // Scale down: drop the first worker
        await workers[0].DisposeAsync();
        workers.RemoveAt(0);

        // NATS-DOC-END
    }
}
