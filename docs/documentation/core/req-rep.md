# Request-Reply Pattern

Request-Reply is a common pattern in modern distributed systems.
A request is sent, and the application either waits on the response with a certain timeout,
or receives a response asynchronously.

Create a service that will be responding to requests:
```csharp
await using var nats = new NatsConnection();

await using var sub = await conn.SubscribeAsync<int>("math.double");

await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    Console.WriteLine($"Received request: {msg.Data}");

    await msg.ReplyAsync($"Answer is: { 2 * msg.Data }");
}
```

Reply to a request is asynchronously received using an _inbox_ subscription
behind the scenes:
```csharp
await using var nats = new NatsConnection();

var reply = await nats.RequestAsync<int, string>("math.double", 2);

Console.WriteLine($"Received reply: {reply}")
```
