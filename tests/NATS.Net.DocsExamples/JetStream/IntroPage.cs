// ReSharper disable SuggestVarOrType_Elsewhere

using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable SA1515

namespace NATS.Net.DocsExamples.JetStream;

public class IntroPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.JetStream.IntroPage");

        try
        {
            await using var nats1 = new NatsConnection();
            var js1 = new NatsJSContext(nats1);
            await js1.DeleteStreamAsync("shop_orders");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        try
        {
            await using var nats1 = new NatsConnection();
            var js1 = new NatsJSContext(nats1);
            await js1.DeleteStreamAsync("orders");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }


        #region js-connection
        await using var nats = new NatsConnection();
        var js = new NatsJSContext(nats);
        #endregion

        #region js-stream
        await js.CreateStreamAsync(new StreamConfig(name: "shop_orders", subjects: new[] { "orders.>" }));
        #endregion

        #region js-serializer
        // Use generated JSON serializer
        var orderSerializer = new NatsJsonContextSerializer<Order>(OrderJsonSerializerContext.Default);
        #endregion

        #region js-publish
        // Publish new order messages
        for (var i = 0; i < 10; i++)
        {
            // Notice we're using JetStream context to publish and receive ACKs
            var ack = await js.PublishAsync($"orders.new.{i}", new Order(i), serializer: orderSerializer);
            ack.EnsureSuccess();
        }
        #endregion

        #region js-consumer
        var consumer = await js.CreateConsumerAsync(stream: "shop_orders", new ConsumerConfig("order_processor"));
        #endregion

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var cancellationToken = cts.Token;

        #region consumer-consume
        await foreach (var msg in consumer.ConsumeAsync<Order>(serializer: orderSerializer).WithCancellation(cancellationToken))
        {
            var order = msg.Data;
            Console.WriteLine($"Processing {msg.Subject} {order}...");
            await msg.AckAsync(cancellationToken: cancellationToken);
            // this loop never ends unless there is an error
        }
        #endregion
    }
}

#region serializer
// Generate serializer context at compile time, ready for native AOT deployments
[JsonSerializable(typeof(Order))]
public partial class OrderJsonSerializerContext : JsonSerializerContext
{
}

public record Order(int OrderId);
#endregion
