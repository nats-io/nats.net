// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Core;

public class PubSubPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Core.PubSubPage");

        {
            #region pubsub
            await using var nats = new NatsConnection();

            var subscription = Task.Run(async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<int>("foo"))
                {
                    Console.WriteLine($"Received {msg.Subject}: {msg.Data}\n");

                    if (msg.Data == -1)
                        break;
                }
            });

            // Give subscription time to start
            await Task.Delay(1000);

            for (var i = 0; i < 10; i++)
            {
                Console.WriteLine($" Publishing {i}...");
                await nats.PublishAsync<int>("foo", i);
            }

            // Signal subscription to stop
            await nats.PublishAsync<int>("foo", -1);

            // Make sure subscription completes cleanly
            await subscription;
            #endregion
        }

        {
            #region lowlevel
            await using var nats = new NatsConnection();

            await using INatsSub<int> sub = await nats.SubscribeCoreAsync<int>("foo");

            // Make sure subscription has reached the server
            await nats.PingAsync();

            for (var i = 0; i < 10; i++)
            {
                Console.WriteLine($" Publishing {i}...");
                await nats.PublishAsync<int>("foo", i);
            }

            // Signal subscription to stop
            await nats.PublishAsync<int>("foo", -1);

            // Messages have been collected in the subscription internal channel
            // now we can drain them
            await foreach (var msg in sub.Msgs.ReadAllAsync())
            {
                Console.WriteLine($"Received {msg.Subject}: {msg.Data}\n");
                if (msg.Data == -1)
                    break;
            }

            // We can unsubscribe from the subscription explicitly
            // (otherwise dispose will do it for us)
            await sub.UnsubscribeAsync();
            #endregion
        }
    }
}
