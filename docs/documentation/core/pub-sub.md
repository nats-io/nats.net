# Publish-Subscribe Pattern

NATS implements a publish-subscribe message distribution model for one-to-many communication.
A publisher sends a message on a subject and any active subscriber listening on that subject
receives the message.

```csharp
await using var nats = new NatsConnection();

var sub = Task.Run(async () =>
{
    await foreach(var msg in nats.SubscribeAsync<int>("foo"))
    {
        Console.WriteLine($"Received {msg.Subject}: {msg.Data}\n");

        if (msg.Data == -1)
            break;
    }
});

for (int i = 0; i < 10; i++)
{
    Console.WriteLine($" Publishing {i}...");
    await nats.PublishAsync<int>("foo", i);
    await Task.Delay(1000);
}

await nats.PublishAsync<int>("foo", -1);

await sub;
```
