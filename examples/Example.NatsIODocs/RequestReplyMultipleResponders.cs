using NATS.Client.Core;
using NATS.Net;

[Collection("nats-server")]
public class RequestReplyMultipleResponders(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        string ProcessCalculation(string? data)
        {
            return "calculated result";
        }

        // Set up 2 instances of the service (no queue group, so both reply to each request)
        var serviceA = await client.Connection.SubscribeCoreAsync<string>("calc.add");
        _ = Task.Run(async () =>
        {
            await foreach (var msg in serviceA.Msgs.ReadAllAsync())
            {
                var result = ProcessCalculation(msg.Data) + $" from A";
                await msg.ReplyAsync(result);
            }
        });

        var serviceB = await client.Connection.SubscribeCoreAsync<string>("calc.add");
        _ = Task.Run(async () =>
        {
            await foreach (var msg in serviceB.Msgs.ReadAllAsync())
            {
                var result = ProcessCalculation(msg.Data) + $" from B";
                await msg.ReplyAsync(result);
            }
        });

        // The first reply wins; later replies are dropped
        var reply = await client.RequestAsync<string>("calc.add");
        Console.WriteLine($"Got response: {reply.Data}");

        // NATS-DOC-END
    }
}
