using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class LearnJetStreamAdvancedPublishingAsync(NatsServerFixture fixture, ITestOutputHelper output)
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

        // The ORDERS stream captures every subject under `orders.`
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        // NATS-DOC-START
        // Async publish: PublishConcurrentAsync sends the message and returns a
        // future without waiting for the ack, so the round trips overlap. Collect
        // the futures, then await each one and call EnsureSuccess -- a future whose
        // ack reports an error is a failed publish you must re-send.
        Order[] orders =
        [
            new Order(OrderId: "ord_8w2k", Customer: "acme-co", TotalCents: 4200),
            new Order(OrderId: "ord_2zr9", Customer: "globex", TotalCents: 7800),
            new Order(OrderId: "ord_5t1m", Customer: "initech", TotalCents: 1500),
            new Order(OrderId: "ord_9p3x", Customer: "hooli", TotalCents: 9900),
        ];

        var futures = new List<NatsJSPublishConcurrentFuture>();
        foreach (var order in orders)
        {
            futures.Add(await js.PublishConcurrentAsync<Order>("orders.created", order));
        }

        for (var i = 0; i < futures.Count; i++)
        {
            await using var future = futures[i];
            var ack = await future.GetResponseAsync();
            ack.EnsureSuccess();
            output.WriteLine($"order {i + 1} stored at sequence {ack.Seq}");
        }

        // NATS-DOC-END
    }
}
