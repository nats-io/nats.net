using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamMessageTtlTtlOnDisabledStream(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(new NatsOpts
        {
            Url = fixture.Server.Url,
            SerializerRegistry = SnakeCaseJsonSerializerRegistry.Default,
        });
        var js = client.CreateJetStreamContext();

        // Start from a clean stream (the test server is shared across the collection)
        try
        {
            await js.DeleteStreamAsync("ORDERS_NO_TTL");
        }
        catch (NatsJSApiException)
        {
            // Stream doesn't exist yet, nothing to delete
        }

        // This stream never opts in to per-message TTLs (no AllowMsgTTL)
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS_NO_TTL", subjects: ["no-ttl.>"]));

        NatsJSApiException? rejection = null;

        // NATS-DOC-START
        // The `Nats-TTL` header only works on a stream created with
        // AllowMsgTTL = true. Publishing it to a stream that hasn't enabled the
        // feature is rejected instead of silently storing the message forever.
        var headers = new NatsHeaders { ["Nats-TTL"] = "60s" };

        try
        {
            var ack = await js.PublishAsync<OrderCancellation>(
                subject: "no-ttl.canceled",
                data: new OrderCancellation(OrderId: "ord_8w2k", Reason: "customer_request"),
                headers: headers);

            // PublishAsync returns the server's answer; EnsureSuccess turns a
            // rejection into an exception instead of leaving it unchecked.
            ack.EnsureSuccess();
        }
        catch (NatsJSApiException ex)
        {
            // 10166: per-message TTL is disabled. The fix is to recreate the
            // stream with AllowMsgTTL = true so it accepts the `Nats-TTL` header.
            rejection = ex;
            output.WriteLine($"Rejected ({ex.Error.Code}): {ex.Error.Description}");
        }

        // NATS-DOC-END
        Assert.NotNull(rejection);
    }
}
