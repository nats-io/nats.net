// ReSharper disable SuggestVarOrType_Elsewhere

using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable SA1515

namespace NATS.Net.DocsExamples.JetStream;

public class PushConsumerPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.JetStream.PushConsumerPage");

        try
        {
            await using NatsConnection nats1 = new();
            NatsJSContext js1 = new(nats1);
            await js1.DeleteStreamAsync("PUSH_ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        #region push-connection
        await using NatsClient nc = new();
        INatsJSContext js = nc.CreateJetStreamContext();
        #endregion

        #region push-stream
        await js.CreateStreamAsync(new StreamConfig(name: "PUSH_ORDERS", subjects: ["orders.>"]));
        #endregion

        #region push-publish
        // Publish new order messages
        for (int i = 0; i < 10; i++)
        {
            // Notice we're using JetStream context to publish and receive ACKs
            PubAckResponse ack = await js.PublishAsync($"orders.new.{i}", new Order { Id = i });
            ack.EnsureSuccess();
        }
        #endregion

        #region push-create-consumer
        INatsJSPushConsumer consumer = await js.CreateOrUpdatePushConsumerAsync(stream: "PUSH_ORDERS", new NatsJSPushConsumerOpts
        {
            Name = "order_push_processor",
            FilterSubject = "orders.>",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            IdleHeartbeat = TimeSpan.FromSeconds(5),
            FlowControl = true,
        });
        #endregion

        CancellationTokenSource cts = new(TimeSpan.FromSeconds(1));
        CancellationToken cancellationToken = cts.Token;

        #region push-consume
        // The server pushes messages to the consumer's delivery subject.
        // ConsumeAsync yields messages as they arrive, just like a pull consumer.
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
