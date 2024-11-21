#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NATS.Net.DocsExamples;

public class IntroPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.IntroPage");

        {
            #region core-nats
            await using var nc = new NatsClient();

            // We will use a cancellation token to stop the subscription
            using var cts = new CancellationTokenSource();

            var subscription = Task.Run(async () =>
            {
                await foreach (var msg in nc.SubscribeAsync<string>(subject: "greet.*", cancellationToken: cts.Token))
                {
                    Console.WriteLine($"Received: {msg.Subject}: {msg.Data}");
                }
            });

            // Give subscription time to start
            await Task.Delay(1000);

            for (var i = 0; i < 10; i++)
            {
                await nc.PublishAsync(subject: $"greet.{i}", data: $"Hello, World! {i}");
            }

            // Give subscription task time to receive messages
            await Task.Delay(1000);

            // Unsubscribe
            await cts.CancelAsync();

            await subscription;
            #endregion
        }

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
            await using var nats = new NatsConnection();
            var js = new NatsJSContext(nats);
            await js.DeleteStreamAsync("ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        {
            #region jetstream
            await using var nc = new NatsClient();
            var js = nc.CreateJetStreamContext();

            // Create a stream to store the messages those subjects start with "orders."
            await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

            for (var i = 0; i < 10; i++)
            {
                // Publish a message to the stream. The message will be stored in the stream
                // because the published subject matches one of the the stream's subjects.
                var ack = await js.PublishAsync(subject: $"orders.new.{i}", data: $"order {i}");

                // Ensure the message is stored in the stream.
                // Returned ack makes the JetStream publish different from the core publish.
                ack.EnsureSuccess();
            }

            // Create a consumer to receive the messages
            var consumer = await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig("order_processor"));

            // We will use a cancellation token to stop the consume loop
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            await foreach (var jsMsg in consumer.ConsumeAsync<string>(cancellationToken: cts.Token))
            {
                Console.WriteLine($"Processed: {jsMsg.Subject}: {jsMsg.Data} ({jsMsg.Metadata?.Sequence.Stream}/{jsMsg.Metadata?.NumPending})");

                // Acknowledge the message is processed and the consumer can move to the next message
                await jsMsg.AckAsync(cancellationToken: cts.Token);
            }
            #endregion
        }
    }
}
