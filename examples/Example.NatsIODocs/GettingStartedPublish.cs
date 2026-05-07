using NATS.Net;

public class GettingStartedPublish
{
    [Fact(Skip = "Targets demo.nats.io; not run in CI.")]
    public async Task RunAsync()
    {
        // NATS-DOC-START
        await using var client = new NatsClient("demo.nats.io");

        // Publish a message to the subject "hello"
        await client.PublishAsync("hello", "Hello NATS!");
        Console.WriteLine("Message published to hello");

        // NATS-DOC-END
    }
}
