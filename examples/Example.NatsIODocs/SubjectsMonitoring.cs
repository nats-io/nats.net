using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class SubjectsMonitoring(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        _ = Task.Run(async () =>
        {
            // NATS-DOC-START
            // Wire tap: subscribe to everything for monitoring
            await foreach (var msg in client.SubscribeAsync<string>(">"))
            {
                output.WriteLine($"[MONITOR] {msg.Subject}: {msg.Data}");
            }

            // NATS-DOC-END
        });

        await Task.Delay(1000);

        await client.PublishAsync("hello", "Hello NATS!");
        await client.PublishAsync("event.new", "click");
        await client.PublishAsync("weather.north.fr", "Temperature: 11C");

        await Task.Delay(1000);
    }
}
