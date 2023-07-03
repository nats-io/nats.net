# NATS.NET V2

## Preview

The NATS.NET V2 client is in preview and not recommended for production use.
Codebase is still under heavy development and currently we only have implementations for [core NATS](https://docs.nats.io/nats-concepts/core-nats) features.

Please test and provide feedback by visiting our [Slack channel](https://natsio.slack.com/channels/dotnet).

## NATS.NET V2 Goals

- Only support Async I/O
- Target latest .NET LTS Release (currently `net6.0`)

## Packages

- **NATS.Client.Core**: [core NATS](https://docs.nats.io/nats-concepts/core-nats)
- **NATS.Client.Hosting**: extension to configure DI container
- **NATS.Client.JetStream**: JetStream *not yet implemented*

## Basic Usage

[Download the latest](https://nats.io/download/) `nats-server` for your platform and run it without any arguments. `nats-server` will listen
on its default TCP port 4222.

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
await using var nats = new NatsConnection(options);

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

You should also hook your logger to `NatsConnection` to make sure all is working as expected or
to get help diagnosing any issues you might have:
```csharp
var options = NatsOptions.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };
await using var nats = new NatsConnection(options);
```

## Contributing

- Run `dotnet format` at root directory of project in order to clear warnings that can be auto-formatted

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
