// ReSharper disable SuggestVarOrType_Elsewhere

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
            await using NatsConnection nats1 = new NatsConnection();
            NatsJSContext js1 = new NatsJSContext(nats1);
            await js1.DeleteStreamAsync("SHOP_ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        try
        {
            await using NatsConnection nats1 = new NatsConnection();
            NatsJSContext js1 = new NatsJSContext(nats1);
            await js1.DeleteStreamAsync("ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        #region js-connection
        await using NatsClient nc = new NatsClient();
        INatsJSContext js = nc.CreateJetStreamContext();
        #endregion

        #region js-stream
        await js.CreateStreamAsync(new StreamConfig(name: "SHOP_ORDERS", subjects: ["orders.>"]));
        #endregion

        #region js-publish
        // Publish new order messages
        for (int i = 0; i < 10; i++)
        {
            // Notice we're using JetStream context to publish and receive ACKs
            PubAckResponse ack = await js.PublishAsync($"orders.new.{i}", new Order { Id = i });
            ack.EnsureSuccess();
        }
        #endregion

        #region js-consumer
        INatsJSConsumer consumer = await js.CreateOrUpdateConsumerAsync(stream: "SHOP_ORDERS", new ConsumerConfig("order_processor"));
        #endregion

        CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        CancellationToken cancellationToken = cts.Token;

        #region consumer-consume
        await foreach (NatsJSMsg<Order> msg in consumer.ConsumeAsync<Order>().WithCancellation(cancellationToken))
        {
            Order? order = msg.Data;
            Console.WriteLine($"Processing {msg.Subject} {order}...");
            await msg.AckAsync(cancellationToken: cancellationToken);
            // this loop never ends unless there is an error
        }
        #endregion
    }
}

#region order-class
public record Order
{
    public int Id { get; init; }
}
#endregion
