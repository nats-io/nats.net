using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamYourFirstStreamInfo(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Make sure the ORDERS stream exists before we ask about it
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // NATS-DOC-START
        // Fetch information about the ORDERS stream
        var stream = await js.GetStreamAsync("ORDERS");

        // Read key fields: name and subjects come from the config, the
        // message count comes from the live stream state
        var info = stream.Info;
        var subjects = string.Join(", ", info.Config.Subjects ?? []);
        output.WriteLine($"Name:     {info.Config.Name}");
        output.WriteLine($"Subjects: {subjects}");
        output.WriteLine($"Messages: {info.State.Messages}");

        // NATS-DOC-END
    }
}
