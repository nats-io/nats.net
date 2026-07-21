using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamSubjectMappingTransform(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Start from a clean stream (the test server is shared across the collection)
        try
        {
            await js.DeleteStreamAsync("ORDERS-SHARDED");
        }
        catch (NatsJSApiException)
        {
            // Stream doesn't exist yet, nothing to delete
        }

        // NATS-DOC-START
        // Ingest orders on `ingest.<customer>` and rewrite the subject on the way
        // in. `partition(3, 1)` hashes the first wildcard token into one of three
        // buckets, so each customer always shards to the same bucket.
        var stream = await js.CreateStreamAsync(new StreamConfig(name: "ORDERS-SHARDED", subjects: ["ingest.*"])
        {
            SubjectTransform = new SubjectTransform
            {
                Src = "ingest.*",
                Dest = "orders.{{partition(3,1)}}.{{wildcard(1)}}",
            },
        });

        output.WriteLine($"Created {stream.Info.Config.Name}");

        // NATS-DOC-END
        Assert.Equal("ORDERS-SHARDED", stream.Info.Config.Name);
    }
}
