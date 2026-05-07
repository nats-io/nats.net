using NATS.Net;

internal static class BasicsPublish
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient();

        // NATS-DOC-START
        // Publish a message to the subject "weather.updates"
        await client.PublishAsync("weather.updates", "Temperature: 72F");

        // NATS-DOC-END
        Console.WriteLine("Message published to weather.updates");
    }
}
