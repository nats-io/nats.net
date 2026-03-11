// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable IDE0007
#pragma warning disable IDE0008

using System.Threading.Channels;
using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Advanced;

public class SlowConsumerPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Advanced.SlowConsumerPage");

        {
            #region message-dropped
            await using NatsConnection nc = new NatsConnection();

            nc.MessageDropped += (sender, args) =>
            {
                Console.WriteLine($"Dropped message on '{args.Subject}' with {args.Pending} pending messages");
                return default;
            };
            #endregion
        }

        {
            #region slow-consumer-detected
            await using NatsConnection nc = new NatsConnection();

            nc.SlowConsumerDetected += (sender, args) =>
            {
                Console.WriteLine($"Slow consumer detected on '{args.Subscription.Subject}'");
                return default;
            };
            #endregion
        }

        {
            #region tuning
            // Set global default capacity
            NatsOpts opts = new NatsOpts { SubPendingChannelCapacity = 4096 };
            await using NatsConnection nc = new NatsConnection(opts);

            // Override capacity for a specific subscription
            NatsSubOpts subOpts = new NatsSubOpts
            {
                ChannelOpts = new NatsSubChannelOpts { Capacity = 8192 },
            };

            await foreach (NatsMsg<string> msg in nc.SubscribeAsync<string>("events.>", opts: subOpts))
            {
                Console.WriteLine($"Received: {msg.Data}");
            }
            #endregion
        }

        {
            #region suppress-warnings
            NatsOpts opts = new NatsOpts
            {
                SuppressSlowConsumerWarnings = true,
            };

            await using NatsConnection nc = new NatsConnection(opts);

            // Events still fire even when log warnings are suppressed
            nc.SlowConsumerDetected += (sender, args) =>
            {
                Console.WriteLine($"Slow consumer on '{args.Subscription.Subject}'");
                return default;
            };
            #endregion
        }
    }
}
