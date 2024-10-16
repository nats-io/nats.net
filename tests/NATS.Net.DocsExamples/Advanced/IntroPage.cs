// ReSharper disable RedundantTypeArgumentsOfMethod
// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Advanced;

public class IntroPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Advanced.IntroPage");

        {
            #region lowlevel-sub
            await using var nc = new NatsConnection();

            // Connections are lazy, so we need to connect explicitly
            // to avoid any races between subscription and publishing.
            await nc.ConnectAsync();

            await using var sub = await nc.SubscribeCoreAsync<int>("foo");

            for (var i = 0; i < 10; i++)
            {
                Console.WriteLine($" Publishing {i}...");
                await nc.PublishAsync<int>("foo", i);
            }

            // Signal subscription to stop
            await nc.PublishAsync<int>("foo", -1);

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

        {
            #region ping
            await using var nc = new NatsClient();

            TimeSpan rtt = await nc.PingAsync();

            Console.WriteLine($"RTT to server: {rtt}");
            #endregion
        }

        {
            #region logging
            using var loggerFactory = LoggerFactory.Create(configure: builder => builder.AddConsole());

            var opts = new NatsOpts { LoggerFactory = loggerFactory };

            await using var nc = new NatsConnection(opts);
            #endregion
        }
    }
}
