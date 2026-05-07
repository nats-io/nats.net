using NATS.Net;

[Collection("nats-server")]
public class BasicsPublish(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Publish a message to the subject "weather.updates"
        await client.PublishAsync("weather.updates", "Temperature: 72F");

        // NATS-DOC-END
        Console.WriteLine("Message published to weather.updates");
    }
}
