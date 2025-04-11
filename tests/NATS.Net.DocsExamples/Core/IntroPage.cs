#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Core;

public class IntroPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Core.IntroPage");

        Task subscription;
        {
            subscription = Task.Run(async () =>
            {
                #region sub
                await using NatsClient nc = new NatsClient();

                await foreach (NatsMsg<Bar> msg in nc.SubscribeAsync<Bar>("bar.>"))
                {
                    if (msg.Subject == "bar.exit")
                        break;

                    Console.WriteLine($"Received {msg.Subject}: {msg.Data}\n");
                }
                #endregion
            });
        }

        await Task.Delay(1000);

        {
            #region pub
            await using NatsClient nc = new NatsClient();

            for (int i = 0; i < 10; i++)
            {
                Console.WriteLine($" Publishing {i}...");
                await nc.PublishAsync<Bar>($"bar.baz.{i}", new Bar(Id: i, Name: "Baz"));
            }

            await nc.PublishAsync("bar.exit");
            #endregion

            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(250);
                await nc.PublishAsync("bar.exit");
            }
        }

        await subscription;
    }
}

#region bar
public record Bar(int Id, string Name);
#endregion
