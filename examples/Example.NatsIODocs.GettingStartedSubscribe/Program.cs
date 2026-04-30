// NATS-DOC-START
using NATS.Net;

await using var client = new NatsClient("demo.nats.io");

Console.WriteLine("Waiting for messages on 'hello'...");

// Subscribe to 'hello' and process messages
await foreach (var msg in client.SubscribeAsync<string>("hello"))
{
    Console.WriteLine($"Received: {msg.Data}");
}

// NATS-DOC-END
