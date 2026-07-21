using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamShapingTheStreamSetLimits(NatsServerFixture fixture, ITestOutputHelper output)
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

        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // NATS-DOC-START
        // Cap ORDERS with a seven-day age limit and a 1 GiB byte ceiling. Fetch
        // the current config, set the limits, and update the stream in place;
        // the stored messages stay put.
        var stream = await js.GetStreamAsync("ORDERS");
        var config = stream.Info.Config;
        config.MaxAge = TimeSpan.FromDays(7);
        config.MaxBytes = 1L << 30; // 1 GiB
        await js.UpdateStreamAsync(config);
        output.WriteLine("ORDERS capped at 7d age and 1 GiB");

        // NATS-DOC-END
        var updated = await js.GetStreamAsync("ORDERS");
        Assert.Equal(1L << 30, updated.Info.Config.MaxBytes);
    }
}
