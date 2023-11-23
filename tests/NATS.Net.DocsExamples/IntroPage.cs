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
            await using var nats = new NatsConnection();

            var cts = new CancellationTokenSource();

            var subscription = Task.Run(async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<string>(subject: "foo").WithCancellation(cts.Token))
                {
                    Console.WriteLine($"Received: {msg.Data}");
                }
            });

            // Give subscription time to start
            await Task.Delay(1000);

            for (var i = 0; i < 10; i++)
            {
                await nats.PublishAsync(subject: "foo", data: $"Hello, World! {i}");
            }

            // Give subscription time to receive messages
            await Task.Delay(1000);

            // Unsubscribe
            cts.Cancel();

            await subscription;
            #endregion
        }

        {
            #region jetstream
            await using var nats = new NatsConnection();
            var js = new NatsJSContext(nats);

            // Create a stream to store the messages
            await js.CreateStreamAsync(new StreamConfig(name: "orders", subjects: new [] { "orders.*" }));

            for (var i = 0; i < 10; i++)
            {
                // Publish a message to the stream. The message will be stored in the stream
                // because the published subject matches one of the the stream's subjects.
                var ack = await js.PublishAsync(subject: "orders.new", data: $"order {i}");
                ack.EnsureSuccess();
            }

            // Create a consumer to receive the messages
            var consumer = await js.CreateConsumerAsync("orders", new ConsumerConfig("order_processor"));

            await foreach (var jsMsg in consumer.ConsumeAsync<string>())
            {
                Console.WriteLine($"Processed: {jsMsg.Data}");
                await jsMsg.AckAsync();

                // Process only 10 messages
                // (message order might be different in different scenarios)
                if (jsMsg.Data == "order 9")
                {
                    break;
                }
            }
            #endregion
        }
    }
}
