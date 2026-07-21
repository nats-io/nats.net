using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamMirrorsAndSourcesCreateMirror(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Start from clean streams (the test server is shared across the collection);
        // the mirror must go before its upstream.
        foreach (var name in new[] { "ORDERS-ARCHIVE", "ORDERS" })
        {
            try
            {
                await js.DeleteStreamAsync(name);
            }
            catch (NatsJSApiException)
            {
                // Stream doesn't exist yet, nothing to delete
            }
        }

        // The upstream the mirror follows must exist first
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // NATS-DOC-START
        // Create ORDERS-ARCHIVE as a read-only mirror of ORDERS. A mirror takes
        // no subjects of its own; it follows the upstream stream.
        var stream = await js.CreateStreamAsync(new StreamConfig(name: "ORDERS-ARCHIVE", subjects: [])
        {
            Mirror = new StreamSource { Name = "ORDERS" },
        });

        // Confirm: the new stream mirrors ORDERS
        output.WriteLine($"Created mirror {stream.Info.Config.Name} of {stream.Info.Config.Mirror!.Name}");

        // NATS-DOC-END
    }
}
