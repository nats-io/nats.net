using NATS.Net;

// NATS-DOC-START
internal static class GettingStartedPublish
{
    public static async Task RunAsync()
    {
        await using var client = new NatsClient("demo.nats.io");

        // Publish a message to the subject "hello"
        await client.PublishAsync("hello", "Hello NATS!");
        Console.WriteLine("Message published to hello");
    }
}

// NATS-DOC-END
