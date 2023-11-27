// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Core;

public class ReqRepPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Core.ReqRepPage");

        var cts = new CancellationTokenSource();

        var sub = Task.Run(async () =>
        {
            #region sub
            await using var nats = new NatsConnection();

            await foreach (var msg in nats.SubscribeAsync<int>("math.double").WithCancellation(cts.Token))
            {
                Console.WriteLine($"Received request: {msg.Data}");

                await msg.ReplyAsync($"Answer is: {2 * msg.Data}");
            }
            #endregion
        });

        await Task.Delay(1000);

        {
            #region reqrep
            await using var nats = new NatsConnection();

            var reply = await nats.RequestAsync<int, string>("math.double", 2);

            Console.WriteLine($"Received reply: {reply.Data}");
            #endregion
        }

        cts.Cancel();

        await sub;
    }
}
