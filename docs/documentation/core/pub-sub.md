# Publish-Subscribe Pattern

NATS implements a publish-subscribe message distribution model for one-to-many communication.
A publisher sends a message on a subject and any active subscriber listening on that subject
receives the message.

```csharp
await using var nats = new NatsConnection();

await using sub = await nats.SubscribeAsync<int>("foo");

for (int i = 0; i < 10; i++)
{
    Console.WriteLine($" Publishing {i}...");
    await nats.PublishAsync<int>("foo", i);
}

await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    Console.WriteLine($"Received {msg.Subject}: {msg.Data}\n");
}
```
