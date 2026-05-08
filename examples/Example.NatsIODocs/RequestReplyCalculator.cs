using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class RequestReplyCalculator(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Calculator service: takes in an array of integers and replies with the sum
        // An integer array would be serialized as a JSON array, while a single integer
        // would be serialized as a string, just like all other primitive types.
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<int[]>("calc.sum"))
            {
                var sum = msg.Data!.Sum();
                await msg.ReplyAsync(sum);
            }
        });

        // Let the subscription register
        await Task.Delay(1000);

        var reply1 = await client.RequestAsync<int[], int>("calc.sum", [5, 3, 1]);
        output.WriteLine($"5 + 3 + 1 = {reply1.Data}");

        var reply2 = await client.RequestAsync<int[], int>("calc.sum", [10, 7]);
        output.WriteLine($"10 + 7 = {reply2.Data}");

        // NATS-DOC-END
    }
}
