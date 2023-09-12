# NATS.NET V2

NATS.NET V2 is a [NATS](https://nats.io) client for the modern [.NET](https://dot.net/).

## Preview

The NATS.NET V2 client is in preview and not recommended for production use yet.
Codebase is still under heavy development and we currently implemented [Core NATS](https://docs.nats.io/nats-concepts/core-nats)
and basic [JetStream](https://docs.nats.io/nats-concepts/jetstream) features.

Please test and provide feedback:

* on [slack.nats.io dotnet channel](https://natsio.slack.com/channels/dotnet)
* or use GitHub discussions, issues and PRs

Thank you to our contributors so far. We feel we are growing slowly as a community and we appreciate your help
supporting and developing NATS .NET V2 project.

## NATS.NET V2 Goals

- Only support Async I/O (async/await)
- Target latest .NET LTS Release (currently .NET 6.0)

## Packages

- **NATS.Client.Core**: [Core NATS](https://docs.nats.io/nats-concepts/core-nats)
- **NATS.Client.Hosting**: extension to configure DI container
- **NATS.Client.JetStream**: [JetStream](https://docs.nats.io/nats-concepts/jetstream)

## Core NATS Quick Start

[Download the latest](https://nats.io/download/) `nats-server` for your platform and run it without any arguments. `nats-server` will listen
on its default TCP port 4222.

```shell
$ nats-server
```

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

## JetStream Quick Start

This time run `nats-server` with JetStream enabled:

```shell
$ nats-server -js
```

Before we can so anything, we need a JetStream context:

```csharp
await using var nc = new NatsConnection();
var js = new NatsJSContext(nc);
```

Let's create our stream first. In JetStream, a stream is simply a storage for messages:

```csharp
await js.CreateStreamAsync(stream: "shop_orders", subjects: new []{"orders.>"});
```

We can save messages in a stream by publishing them to the subjects the stream is interested in, which is `orders.>` in
our case, meaning any subject prefixed with `orders.` e.g. `orders.new.123`. Have a look at NATS documentation about
[wildcards in Subject-Based Messaging](https://docs.nats.io/nats-concepts/subjects#wildcards) for more information.

Given that we have a record `Order`, we can publish and consume stream of `Order` objects:

```csharp
public record Order(int OrderId);
```

We can publish to the `shop_orders` stream and receive a confirmation that our message is persisted:

```csharp
for (var i = 0; i < 10; i++)
{
    // Notice we're using JetStream context to publish and receive ACKs
    var ack = await js.PublishAsync($"orders.new.{i}", new Order(i));
    ack.EnsureSuccess();
}
```

Now that we have a few messages in our stream, let's see its status using the [NATS command
line client](https://github.com/nats-io/natscli):

```shell
$ nats stream ls
╭───────────────────────────────────────────────────────────────────────────────────╮
│                                      Streams                                      │
├─────────────┬─────────────┬─────────────────────┬──────────┬───────┬──────────────┤
│ Name        │ Description │ Created             │ Messages │ Size  │ Last Message │
├─────────────┼─────────────┼─────────────────────┼──────────┼───────┼──────────────┤
│ shop_orders │             │ 2023-09-12 10:25:52 │ 10       │ 600 B │ 10.41s       │
╰─────────────┴─────────────┴─────────────────────┴──────────┴───────┴──────────────╯
```

We need one more JetStream construct before we can start consuming our messages: a *consumer*:

```csharp
var consumer = await js.CreateConsumerAsync(stream: "shop_orders", consumer: "order_processor");
```

In JetStream, consumers are stored on the server. Clients don't need to worry about maintaining state separately.
You can think of JetStream consumers as pointers to messages in streams stored on the NATS JetStream server. Let's
see what our consumer's state is:

```shell
$ nats consumer report shop_orders
╭────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮
│                                Consumer report for shop_orders with 1 consumers                                │
├─────────────────┬──────┬────────────┬──────────┬─────────────┬─────────────┬─────────────┬───────────┬─────────┤
│ Consumer        │ Mode │ Ack Policy │ Ack Wait │ Ack Pending │ Redelivered │ Unprocessed │ Ack Floor │ Cluster │
├─────────────────┼──────┼────────────┼──────────┼─────────────┼─────────────┼─────────────┼───────────┼─────────┤
│ order_processor │ Pull │ Explicit   │ 30.00s   │ 0           │ 0           │ 10 / 100%   │ 0         │         │
╰─────────────────┴──────┴────────────┴──────────┴─────────────┴─────────────┴─────────────┴───────────┴─────────╯
```

Check out [JetStream documentation](https://docs.nats.io/nats-concepts/jetstream) for more information on streams and consumers.

Finally, we're ready to consume the messages we persisted in `shop_orders` stream:

```csharp
await foreach (var msg in consumer.ConsumeAllAsync<Order>())
{
    var order = msg.Data;
    Console.WriteLine($"Processing {msg.Subject} {order}...");
    await msg.AckAsync();
    // this loop never ends unless there is an error
}
```

## Logging

You should also hook your logger to `NatsConnection` to make sure all is working as expected or
to get help diagnosing any issues you might have:

```csharp
var opts = NatsOpts.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };
await using var nats = new NatsConnection(otps);
```

## Contributing

- Run `dotnet format` at root directory of project in order to clear warnings that can be auto-formatted

## Roadmap

- [x] Core NATS
- [x] JetStream initial support
- [ ] KV initial support
- [ ] Object Store initial support
- [ ] .NET 8.0 support (e.g. Native AOT)
- [ ] Beta phase

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
