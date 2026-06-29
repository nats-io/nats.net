using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamShapingTheStreamPerSubjectLimit(NatsServerFixture fixture, ITestOutputHelper output)
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
        // Add a per-subject ceiling so one noisy subject can't evict another's
        // messages. MaxMsgsPerSubject keeps the most recent N messages for every
        // subject independently, alongside the whole-stream limits.
        var stream = await js.GetStreamAsync("ORDERS");
        var config = stream.Info.Config;
        config.MaxMsgsPerSubject = 100000;
        await js.UpdateStreamAsync(config);
        output.WriteLine("ORDERS now keeps 100000 messages per subject");

        // NATS-DOC-END
        var updated = await js.GetStreamAsync("ORDERS");
        Assert.Equal(100000, updated.Info.Config.MaxMsgsPerSubject);
    }
}
