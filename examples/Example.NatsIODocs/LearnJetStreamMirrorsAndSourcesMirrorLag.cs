using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamMirrorsAndSourcesMirrorLag(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var js = client.CreateJetStreamContext();

        // Make sure the upstream and the mirror exist before we ask about lag
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS-ARCHIVE", subjects: [])
        {
            Mirror = new StreamSource { Name = "ORDERS" },
        });

        // NATS-DOC-START
        // A mirror is eventually consistent. Read its lag before trusting it to
        // hold what the upstream just received: 0 means fully caught up.
        var stream = await js.GetStreamAsync("ORDERS-ARCHIVE");
        var mirror = stream.Info.Mirror!;

        output.WriteLine($"Upstream:  {mirror.Name}");
        output.WriteLine($"Lag:       {mirror.Lag}");
        output.WriteLine($"Last seen: {mirror.Active}");

        // NATS-DOC-END
    }
}
