// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Core;

public class QueuePage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Core.QueuePage");

        #region queue
        await using var nats = new NatsConnection();

        var replyTasks = new List<Task>();
        var cts = new CancellationTokenSource();

        for (var i = 0; i < 3; i++)
        {
            // Create three subscriptions all on the same queue group
            // Create a background message loop for every subscription
            var replyTaskId = i;
            replyTasks.Add(Task.Run(async () =>
            {
                // Retrieve messages until unsubscribed
                await foreach (var msg in nats.SubscribeAsync<int>("math.double", queueGroup: "maths-service", cancellationToken: cts.Token))
                {
                    Console.WriteLine($"[{replyTaskId}] Received request: {msg.Data}");
                    await msg.ReplyAsync($"Answer is: {2 * msg.Data}");
                }

                Console.WriteLine($"[{replyTaskId}] Done");
            }));
        }

        // Give subscriptions time to start
        await Task.Delay(1000);

        // Send a few requests
        for (var i = 0; i < 10; i++)
        {
            var reply = await nats.RequestAsync<int, string>("math.double", i);
            Console.WriteLine($"Reply: '{reply.Data}'");
        }

        Console.WriteLine("Stopping...");

        // Cancellation token will unsubscribe and complete the message loops
        cts.Cancel();

        // Make sure all tasks finished cleanly
        await Task.WhenAll(replyTasks);

        Console.WriteLine("All done");
        #endregion
    }
}
