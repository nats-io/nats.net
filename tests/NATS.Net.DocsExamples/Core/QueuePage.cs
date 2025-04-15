// ReSharper disable MethodSupportsCancellation
// ReSharper disable AccessToDisposedClosure
// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable IDE0007
#pragma warning disable IDE0008

using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Core;

public class QueuePage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Core.QueuePage");

        #region queue
        await using NatsClient nc = new NatsClient();

        // Create a cancellation token source to stop the subscriptions
        using CancellationTokenSource cts = new CancellationTokenSource();

        List<Task> replyTasks = new List<Task>();

        for (int i = 0; i < 3; i++)
        {
            // Create three subscriptions all on the same queue group
            // Create a background message loop for every subscription
            int replyTaskId = i;
            replyTasks.Add(Task.Run(async () =>
            {
                // Retrieve messages until unsubscribed
                await foreach (NatsMsg<int> msg in nc.SubscribeAsync<int>("math.double", queueGroup: "maths-service", cancellationToken: cts.Token))
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
        for (int i = 0; i < 10; i++)
        {
            NatsMsg<string> reply = await nc.RequestAsync<int, string>("math.double", i);
            Console.WriteLine($"Reply: '{reply.Data}'");
        }

        Console.WriteLine("Stopping...");

        // Cancellation token will unsubscribe and complete the message loops
        await cts.CancelAsync();

        // Make sure all tasks finished cleanly
        await Task.WhenAll(replyTasks);

        Console.WriteLine("All done");
        #endregion
    }
}
