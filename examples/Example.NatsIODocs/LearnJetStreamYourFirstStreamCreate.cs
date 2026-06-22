using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamYourFirstStreamCreate(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // NATS-DOC-START
        // Create the ORDERS stream, capturing every subject under `orders.`
        var stream = await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // Confirm the stream was created
        output.WriteLine($"Created stream: {stream.Info.Config.Name}");

        // NATS-DOC-END
    }
}
