# Queue Groups

When subscribers register themselves to receive messages from a publisher,
the 1:N fan-out pattern of messaging ensures that any message sent by a publisher,
reaches all subscribers that have registered. NATS provides an additional feature
named "queue", which allows subscribers to register themselves as part of a queue.
Subscribers that are part of a queue, form the "queue group".

Code below demonstrates multiple subscriptions on the same queue group,
receiving messages randomly distributed among them. This example also shows
how queue groups can be used to load balance responders:

```csharp
using NATS.Client.Core;

await using var nats = new NatsConnection();

var subs = new List<NatsSubBase>();
var replyTasks = new List<Task>();

for (int i = 0; i < 3; i++)
{
    // Create three subscriptions all on the same queue group
    var sub = await nats.SubscribeAsync<int>("math.double", queueGroup: "maths-service");

    subs.Add(sub);

    // Create a background message loop for every subscription
    var replyTaskId = i;
    replyTasks.Add(Task.Run(async () =>
    {
        // Retrieve messages until unsubscribed
        await foreach (var msg in sub.Msgs.ReadAllAsync())
        {
            Console.WriteLine($"[{replyTaskId}] Received request: {msg.Data}");
            await msg.ReplyAsync($"Answer is: {2 * msg.Data}");
        }

        Console.WriteLine($"[{replyTaskId}] Done");
    }));
}

// Send a few requests
for (int i = 0; i < 10; i++)
{
    var reply = await nats.RequestAsync<int, string>("math.double", i);
    Console.WriteLine($"Reply: '{reply}'");
}

Console.WriteLine("Stopping...");

// Unsubscribing or disposing will complete the message loops
foreach (var sub in subs)
    await sub.UnsubscribeAsync();

// Make sure all tasks finished cleanly
await Task.WhenAll(replyTasks);

Console.WriteLine("Bye");
```

Output should look similar to this:

```
[0] Received request: 0
Reply: 'Answer is: 0'
[2] Received request: 1
Reply: 'Answer is: 2'
[1] Received request: 2
Reply: 'Answer is: 4'
[0] Received request: 3
Reply: 'Answer is: 6'
[0] Received request: 4
Reply: 'Answer is: 8'
[1] Received request: 5
Reply: 'Answer is: 10'
[2] Received request: 6
Reply: 'Answer is: 12'
[0] Received request: 7
Reply: 'Answer is: 14'
[1] Received request: 8
Reply: 'Answer is: 16'
[0] Received request: 9
Reply: 'Answer is: 18'
Stopping...
[0] Done
[1] Done
[2] Done
Bye
```
