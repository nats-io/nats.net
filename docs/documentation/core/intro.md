# Core NATS

[Core NATS](https://docs.nats.io/nats-concepts/core-nats) is the base set of functionalities and qualities of service
offered by a NATS service infrastructure. Core NATS is the foundation for JetStream and other services. For the sake
of explanation, in a simplified sense you can think of Core NATS as the
[wire protocol](https://docs.nats.io/reference/reference-protocols/nats-protocol) defining a simple but powerful
pub/sub functionality and the concept of [Subject-Based Messaging](https://docs.nats.io/nats-concepts/subjects).

## Core NATS Quick Start

[Download the latest](https://nats.io/download/) `nats-server` for your platform and run it without any arguments. `nats-server` will listen
on its default TCP port 4222.

```shell
$ nats-server
```

Install `NATS.Client.Core` preview from Nuget.

Given that we have a plain class `Bar`, we can publish and subscribe to our `nats-server` sending
and receiving `Bar` objects:

```csharp
public record Bar
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```

Subscribe to all `bar` [related subjects](https://docs.nats.io/nats-concepts/subjects):

```csharp
await using var nats = new NatsConnection();

await using sub = await nats.SubscribeAsync<Bar>("bar.>");
await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    Console.WriteLine($"Received {msg.Subject}: {msg.Data}\n");
}
```

Publish `Bar` objects to related `bar` [subjects](https://docs.nats.io/nats-concepts/subjects):
```csharp
await using var nats = new NatsConnection();

for (int i = 0; i < 10; i++)
{
    Console.WriteLine($" Publishing {i}...");
    await nats.PublishAsync<Bar>($"bar.baz.{i}", new Bar { Id = i, Name = "Baz" });
}
```

## Logging

You should also hook your logger to `NatsConnection` to make sure all is working as expected or
to get help diagnosing any issues you might have:

```csharp
var opts = NatsOpts.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };
await using var nats = new NatsConnection(otps);
```

## What's Next

[Publish-Subscribe](pub-sub.md) is the message distribution model for one-to-many communication.

[Request-Reply](req-rep.md) is a common pattern in modern distributed systems. A request is sent, and the application
either waits on the response with a certain timeout, or receives a response asynchronously.

[Queue Groups](queue.md) enables the 1:N fan-out pattern of messaging ensuring that any message sent by a publisher,
reaches all subscribers that have registered.
