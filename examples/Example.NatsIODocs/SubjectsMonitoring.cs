using NATS.Net;

[Collection("nats-server")]
public class SubjectsMonitoring(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Subscribe to everything; run in the background so we can publish below
        var subscribe = Task.Run(async () =>
        {
            var received = 0;
            await foreach (var msg in client.SubscribeAsync<string>(">"))
            {
                Console.WriteLine($"[MONITOR] {msg.Subject} --> {msg.Data}");
                if (++received == 3)
                {
                    break;
                }
            }
        });

        // NATS-DOC-END
        await client.PingAsync();

        await client.PublishAsync("hello", "Hello NATS!");
        await client.PublishAsync("event.new", "click");
        await client.PublishAsync("weather.north.fr", "Temperature: 11C");

        Console.WriteLine("Waiting for messages...");
        await subscribe;
    }
}
