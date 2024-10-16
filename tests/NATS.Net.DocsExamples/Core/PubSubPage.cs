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
            await using var nc = new NatsClient();

            var subscription = Task.Run(async () =>
            {
                await foreach (var msg in nc.SubscribeAsync<int>("foo"))
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
                await nc.PublishAsync<int>("foo", i);
            }

            // Signal subscription to stop
            await nc.PublishAsync<int>("foo", -1);

            // Make sure subscription completes cleanly
            await subscription;
            #endregion
        }
    }
}
