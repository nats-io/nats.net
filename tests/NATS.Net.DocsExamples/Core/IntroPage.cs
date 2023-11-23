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
                // required to serialize ad-hoc types
                var opts = new NatsOpts { SerializerRegistry = NatsJsonSerializerRegistry.Default };

                await using var nats = new NatsConnection(opts);

                await foreach (var msg in nats.SubscribeAsync<Bar>("bar.>"))
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
            var opts = new NatsOpts { SerializerRegistry = NatsJsonSerializerRegistry.Default };
            await using var nats = new NatsConnection(opts);

            for (var i = 0; i < 10; i++)
            {
                Console.WriteLine($" Publishing {i}...");
                await nats.PublishAsync<Bar>($"bar.baz.{i}", new Bar(Id: i, Name: "Baz"));
            }

            await nats.PublishAsync("bar.exit");
            #endregion
        }

        await subscription;

        {
            #region logging
            using var loggerFactory = LoggerFactory.Create(configure: builder => builder.AddConsole());

            var opts = new NatsOpts { LoggerFactory = loggerFactory };

            await using var nats = new NatsConnection(opts);
            #endregion
        }
    }
}

#region bar
public record Bar(int Id, string Name);
#endregion
