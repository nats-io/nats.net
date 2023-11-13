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

## Subscriptions with Lower Level Control

The `SubscribeAsync()` method is a convenient way to subscribe to a subject and receive messages without much effort.
If you need more control over how subscription is handled, you can use the `SubscribeCoreAsync()` method instead.

```csharp
await using var nats = new NatsConnection();

await using sub = await nats.SubscribeAsync<int>("foo");

for (int i = 0; i < 10; i++)
{
    Console.WriteLine($" Publishing {i}...");
    await nats.PublishAsync<int>("foo", i);
}

await nats.PublishAsync<int>("foo", -1);

await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    Console.WriteLine($"Received {msg.Subject}: {msg.Data}\n");
    if (msg.Data == -1)
        break;
}

await sub.UnsubscribeAsync();
```
