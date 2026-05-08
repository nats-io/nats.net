using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class BasicsSubscribe(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // Subscribe to 'weather.updates' and process messages
            await foreach (var msg in client.SubscribeAsync<string>("weather.updates"))
            {
                output.WriteLine($"Received: {msg.Data}");
            }

            // NATS-DOC-END
        });

        await Task.Delay(1000);
        await client.PublishAsync("weather.updates", "sunny");
    }
}
