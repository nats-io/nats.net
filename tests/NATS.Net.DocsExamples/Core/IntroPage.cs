#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;

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
                await using var nc = new NatsClient();

                await foreach (var msg in nc.SubscribeAsync<Bar>("bar.>"))
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
            await using var nc = new NatsClient();

            for (var i = 0; i < 10; i++)
            {
                Console.WriteLine($" Publishing {i}...");
                await nc.PublishAsync<Bar>($"bar.baz.{i}", new Bar(Id: i, Name: "Baz"));
            }

            await nc.PublishAsync("bar.exit");
            #endregion

            for (var i = 0; i < 3; i++)
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
