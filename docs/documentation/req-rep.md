# Request-Reply Pattern

Request-Reply is a common pattern in modern distributed systems.
A request is sent, and the application either waits on the response with a certain timeout,
or receives a response asynchronously.

```csharp
await using var nats = new NatsConnection();

await using var replyHandle = await nats.ReplyAsync<int, string>("math.double", x =>
{
    Console.WriteLine($"Received request: {x}")
    return $"Answer is: { 2 * x }";
});

var reply = await nats.RequestAsync<int, string>("math.double", 2);
Console.WriteLine($"Received reply: {reply}")
```
