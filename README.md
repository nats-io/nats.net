[![License Apache 2.0](https://img.shields.io/badge/License-Apache2-blue.svg)](https://www.apache.org/licenses/LICENSE-2.0)
[![NuGet](https://img.shields.io/nuget/v/NATS.Net.svg?cacheSeconds=3600)](https://www.nuget.org/packages/NATS.Net)

# NATS .NET

NATS .NET is a client library designed to connect to the NATS messaging server,
fully supporting all NATS features.
It integrates seamlessly with modern .NET asynchronous interfaces such as
async enumerables and channels, and leverages advanced .NET memory, buffer and IO features.

Check out [NATS .NET client library documentation](https://nats-io.github.io/nats.net.v2/) for guides and examples.

### What is NATS?

NATS is a high-performance, secure, distributed messaging system.
It's a connective technology tailored for modern distributed systems,
facilitating efficient addressing, discovery, and message exchange.
It supports dynamic service and stream processing across various locations and devices,
enhancing mobility, security, and independence from traditional constraints such as DNS.

Head over to [NATS documentation](https://docs.nats.io/nats-concepts/overview) for more information.

## Quick Start

Basic messaging:

```csharp
// NATS core M:N messaging example
await using var nats = new NatsConnection();

// Subscribe on one terminal
await foreach (var msg in nats.SubscribeAsync<string>(subject: "foo"))
{
    Console.WriteLine($"Received: {msg.Data}");
}

// Start publishing to the same subject on a second terminal
await nats.PublishAsync(subject: "foo", data: "Hello, World!");
```

Persistance with JetStream:

```csharp
await using var nats = new NatsConnection();
var js = new NatsJSContext(nats);

// Create a stream to store the messages
await js.CreateStreamAsync(new StreamConfig(name: "orders", subjects: new[] { "orders.*" }));

// Publish a message to the stream. The message will be stored in the stream
// because the published subject matches one of the the stream's subjects.
var ack = await js.PublishAsync(subject: "orders.new", data: "order 1");
ack.EnsureSuccess();

// Create a consumer on a stream to receive the messages
var consumer = await js.CreateOrUpdateConsumerAsync("orders", new ConsumerConfig("order_processor"));

await foreach (var jsMsg in consumer.ConsumeAsync<string>())
{
    Console.WriteLine($"Processed: {jsMsg.Data}");
    await jsMsg.AckAsync();
}
```

See more details, including how to download and start NATS server and JetStream in our [documentation](https://nats-io.github.io/nats.net.v2/documentation/intro.html).

**Additionally check out [NATS by example](https://natsbyexample.com) - An evolving collection of runnable, cross-client reference examples for NATS.**

## NATS .NET Goals

- Only support Async I/O (async/await)
- Target .NET Standard 2.0, 2.1, and .NET LTS releases (currently .NET 6.0 & .NET 8.0)

## Packages

- **NATS.Net**: Meta package that includes all other packages (except serialization)
- **NATS.Client.Core**: [Core NATS](https://docs.nats.io/nats-concepts/core-nats)
- **NATS.Client.JetStream**: [JetStream](https://docs.nats.io/nats-concepts/jetstream)
- **NATS.Client.KeyValueStore**: [Key/Value Store](https://docs.nats.io/nats-concepts/jetstream/key-value-store)
- **NATS.Client.ObjectStore**: [Object Store](https://docs.nats.io/nats-concepts/jetstream/obj_store)
- **NATS.Client.Services**: [Services](https://docs.nats.io/using-nats/developer/services)
- **NATS.Extensions.Microsoft.DependencyInjection**: extension to configure DI container
- **NATS.Client.Serializers.Json**: JSON serializer for ad-hoc types

## Contributing

- Run `dotnet format` at root directory of project in order to clear warnings that can be auto-formatted
- Run `dotnet build` at root directory and make sure there are no errors or warnings

Find us on [slack.nats.io dotnet channel](https://natsio.slack.com/channels/dotnet)

Please also check out the [Contributor Guide](CONTRIBUTING.md) and [Code of Conduct](CODE-OF-CONDUCT.md).

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
