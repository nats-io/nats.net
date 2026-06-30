using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamMirrorsAndSourcesCreateSource(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Setup: the three regional streams ALL-ORDERS aggregates
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS-US", subjects: ["us.orders.>"]));
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS-EU", subjects: ["eu.orders.>"]));
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS-APAC", subjects: ["apac.orders.>"]));

        // NATS-DOC-START
        // Create ALL-ORDERS as an aggregate that sources the three regional
        // streams into one. Unlike a mirror, a stream can list several sources.
        var stream = await js.CreateStreamAsync(new StreamConfig(name: "ALL-ORDERS", subjects: [])
        {
            Sources =
            [
                new StreamSource { Name = "ORDERS-US" },
                new StreamSource { Name = "ORDERS-EU" },
                new StreamSource { Name = "ORDERS-APAC" },
            ],
        });

        // Confirm: the aggregate lists three sources
        output.WriteLine($"Created {stream.Info.Config.Name} sourcing {stream.Info.Config.Sources!.Count} streams");

        // NATS-DOC-END
    }
}
