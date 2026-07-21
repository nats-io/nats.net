using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnCoreNatsPublishSubscribePublish(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);
        var sub = await client.Connection.SubscribeCoreAsync<string>("orders.created");

        // NATS-DOC-START
        // Publish one order to the orders.created subject. Publishing is
        // fire-and-forget: the call hands the message to the server and returns.
        var order = """{"order_id":"ord_8w2k","customer":"acme-co","total_cents":4200,"ts":"2026-05-22T10:14:22Z"}""";
        await client.PublishAsync("orders.created", order);

        // NATS-DOC-END
        var msg = await sub.Msgs.ReadAsync();
        output.WriteLine($"warehouse received: {msg.Data}");
    }
}
