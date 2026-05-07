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
        // Calculator service: parses "x y" and replies with x+y
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("calc.add"))
            {
                var parts = msg.Data!.Split(' ');
                var x = int.Parse(parts[0]);
                var y = int.Parse(parts[1]);
                await msg.ReplyAsync((x + y).ToString());
            }
        });

        // Let the subscription register
        await Task.Delay(1000);

        var reply1 = await client.RequestAsync<string, string>("calc.add", "5 3");
        output.WriteLine($"5 + 3 = {reply1.Data}");

        var reply2 = await client.RequestAsync<string, string>("calc.add", "10 7");
        output.WriteLine($"10 + 7 = {reply2.Data}");

        // NATS-DOC-END
    }
}
