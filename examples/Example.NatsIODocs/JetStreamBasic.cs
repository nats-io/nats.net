using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class JetStreamBasic(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Create a stream that captures any subject under `orders.`
        var js = client.CreateJetStreamContext();
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"])
        {
            Storage = StreamConfigStorage.File,
        });

        // Publish a few orders
        await js.PublishAsync<string>(subject: "orders.new", data: "Order #1001");
        await js.PublishAsync<string>(subject: "orders.new", data: "Order #1002");
        await js.PublishAsync<string>(subject: "orders.shipped", data: "Order #1001 shipped");

        // Create a durable pull consumer that delivers from the beginning
        var consumer = await js.CreateOrUpdateConsumerAsync(stream: "ORDERS", config: new ConsumerConfig
        {
            Name = "order-processor",
            DurableName = "order-processor",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
        });

        // Fetch a batch and acknowledge each message
        await foreach (var msg in consumer.FetchAsync<string>(new NatsJSFetchOpts { MaxMsgs = 3 }))
        {
            output.WriteLine($"Received on {msg.Subject}: {msg.Data}");
            await msg.AckAsync();
        }

        // NATS-DOC-END
    }
}
