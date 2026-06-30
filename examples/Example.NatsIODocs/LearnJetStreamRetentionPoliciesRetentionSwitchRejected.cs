using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamRetentionPoliciesRetentionSwitchRejected(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Start from a clean stream (the test server is shared across the collection)
        try
        {
            await js.DeleteStreamAsync("FULFILLMENT");
        }
        catch (NatsJSApiException)
        {
            // Stream doesn't exist yet, nothing to delete
        }

        // FULFILLMENT is created as a WorkQueue stream
        await js.CreateStreamAsync(new StreamConfig(name: "FULFILLMENT", subjects: ["fulfill.>"])
        {
            Retention = StreamConfigRetention.Workqueue,
        });

        // NATS-DOC-START
        // Try to turn FULFILLMENT into a plain Limits stream after the fact.
        var stream = await js.GetStreamAsync("FULFILLMENT");
        var config = stream.Info.Config;
        config.Retention = StreamConfigRetention.Limits;

        try
        {
            await js.UpdateStreamAsync(config);
            output.WriteLine("update accepted");
        }
        catch (NatsJSApiException e)
        {
            // The server won't switch retention into or out of WorkQueue on a live
            // stream. To change it, recreate the stream.
            output.WriteLine($"rejected (err {e.Error.ErrCode}): {e.Error.Description}");
        }

        // NATS-DOC-END
        var unchanged = await js.GetStreamAsync("FULFILLMENT");
        Assert.Equal(StreamConfigRetention.Workqueue, unchanged.Info.Config.Retention);
    }
}
