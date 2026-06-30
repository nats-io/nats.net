using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamGetDirectEnable(NatsServerFixture fixture, ITestOutputHelper output)
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

        // The ORDERS stream starts without direct get enabled
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        var stream = await js.GetStreamAsync("ORDERS");

        // NATS-DOC-START
        // Turn on direct get by updating the stream config. Read the current
        // config, set AllowDirect, and send the update back.
        var config = stream.Info.Config;
        config.AllowDirect = true;

        var updated = await js.UpdateStreamAsync(config);

        output.WriteLine($"AllowDirect: {updated.Info.Config.AllowDirect}");

        // NATS-DOC-END
        Assert.True(updated.Info.Config.AllowDirect);
    }
}
