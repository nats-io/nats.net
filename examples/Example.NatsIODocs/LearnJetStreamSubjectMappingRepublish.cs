using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamSubjectMappingRepublish(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Start from a clean stream (the test server is shared across the collection)
        try
        {
            await js.DeleteStreamAsync("ORDERS");
        }
        catch (NatsJSApiException)
        {
            // Stream doesn't exist yet, nothing to delete
        }

        // The ORDERS stream starts without republish configured
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        var stream = await js.GetStreamAsync("ORDERS");

        // NATS-DOC-START
        // Mirror every stored order onto a `dash.orders.>` subject as it lands.
        // Read the current config, add the republish rule, and send it back.
        var config = stream.Info.Config;
        config.Republish = new Republish
        {
            Src = "orders.>",
            Dest = "dash.orders.>",

            // HeadersOnly = true,
        };

        var updated = await js.UpdateStreamAsync(config);

        output.WriteLine($"Republishing to {updated.Info.Config.Republish!.Dest}");

        // NATS-DOC-END
        Assert.Equal("dash.orders.>", updated.Info.Config.Republish!.Dest);
    }
}
