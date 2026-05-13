using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class BasicsPublish(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        var sub = await client.Connection.SubscribeCoreAsync<string>("weather.updates");

        // NATS-DOC-START
        // Publish a message to the subject "weather.updates"
        await client.PublishAsync("weather.updates", "Temperature: 72F");

        // NATS-DOC-END
        var msg = await sub.Msgs.ReadAsync();
        output.WriteLine($"Received: {msg.Data}");
    }
}
