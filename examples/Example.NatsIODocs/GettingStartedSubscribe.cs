using NATS.Net;

namespace Example.NatsIODocs;

public class GettingStartedSubscribe(ITestOutputHelper output)
{
    [Fact(Skip = "Targets demo.nats.io and blocks waiting for messages; not run in CI.")]
    public async Task RunAsync()
    {
        // NATS-DOC-START
        await using var client = new NatsClient("demo.nats.io");

        // Subscribe to 'hello' and process messages
        await foreach (var msg in client.SubscribeAsync<string>("hello"))
        {
            output.WriteLine($"Received: {msg.Data}");
        }

        // NATS-DOC-END
    }
}
