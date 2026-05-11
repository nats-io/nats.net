[![License Apache 2.0](https://img.shields.io/badge/License-Apache2-blue.svg)](https://www.apache.org/licenses/LICENSE-2.0)
[![NuGet](https://img.shields.io/nuget/v/NATS.Net.svg?cacheSeconds=3600)](https://www.nuget.org/packages/NATS.Net)
[![Doc](https://img.shields.io/badge/Doc-reference-blue)](https://nats-io.github.io/nats.net/)
[![Test Linux](https://github.com/nats-io/nats.net/actions/workflows/test_linux.yml/badge.svg?branch=main)](https://github.com/nats-io/nats.net/actions/workflows/test_linux.yml?query=branch%3Amain)
[![Test Windows](https://github.com/nats-io/nats.net/actions/workflows/test_windows.yml/badge.svg?branch=main)](https://github.com/nats-io/nats.net/actions/workflows/test_windows.yml?query=branch%3Amain)
[![Slack](https://img.shields.io/badge/chat-on%20slack-green)](https://slack.nats.io)

# NATS .NET

NATS .NET is the .NET client for NATS, a distributed messaging system.
It provides pub/sub and request/reply (Core NATS), streaming and persistence (JetStream),
Key-Value Store, Object Store, and Services.

### Check out [DOCS](https://nats-io.github.io/nats.net/) for guides and examples.

**Additionally check out [NATS by example](https://natsbyexample.com) - An evolving collection of runnable, cross-client reference examples for NATS.**

### Quick Start

Start a NATS server:

```shell
docker run -p 4222:4222 nats
```

Create a subscriber app:

```shell
dotnet new console -n Sub && cd Sub && dotnet add package NATS.Net
```

```csharp
using NATS.Net;

await using var nc = new NatsClient();

await foreach (var msg in nc.SubscribeAsync<string>("greet"))
    Console.WriteLine($"Received: {msg.Data}");
```

In another terminal, create a publisher app:

```shell
dotnet new console -n Pub && cd Pub && dotnet add package NATS.Net
```

```csharp
using NATS.Net;

await using var nc = new NatsClient();

await nc.PublishAsync("greet", "Hello, NATS!");
```

### API at a Glance

```csharp
using NATS.Net;

await using var nc = new NatsClient();

// Publish a message
await nc.PublishAsync("orders.new", new Order(Id: 1, Item: "widget"));

// Subscribe with async enumerable
await foreach (var msg in nc.SubscribeAsync<Order>("orders.>"))
    Console.WriteLine($"Received order: {msg.Data}");

// Request-reply
var order = new Order(Id: 2, Item: "gadget");
var reply = await nc.RequestAsync<Order, Confirmation>("orders.create", order);

// JetStream (persistent messaging)
var js = nc.CreateJetStreamContext();

// Key/Value Store
var kv = nc.CreateKeyValueStoreContext();

// Object Store
var obj = nc.CreateObjectStoreContext();

// Services
var svc = nc.CreateServicesContext();
```

> [!NOTE]
> **We are not testing with .NET 6.0 target anymore** even though it is still targeted by the library.
> This is to reduce the number of test runs and speed up the CI process as well as to prepare for
> the next major version, possibly dropping .NET 6.0 support.

> [!NOTE]
> **Don't confuse NuGet packages!**
> NATS .NET package on NuGet is called [NATS.Net](https://www.nuget.org/packages/NATS.Net).
> There is another package called `NATS.Client` which is the older version of the client library
> and will be deprecated eventually.

> [!TIP]
> NATS .NET now supports **.NET Standard** 2.0 and 2.1 along with .NET 6.0 and 8.0,
> which means you can also use it with **.NET Framework** 4.6.2+ and **Unity** 2018.1+.

### What is NATS?

NATS is a high-performance, secure, distributed messaging system.
It's a connective technology tailored for modern distributed systems,
facilitating efficient addressing, discovery, and message exchange.
It supports dynamic service and stream processing across various locations and devices,
enhancing mobility, security, and independence from traditional constraints such as DNS.

Head over to [NATS documentation](https://docs.nats.io/nats-concepts/overview) for more information.

## NATS .NET Goals

- Only support Async I/O (async/await)
- Target .NET Standard 2.0, 2.1, and the two most recent .NET LTS releases (currently .NET 6.0 & .NET 8.0)

## Packages

- **NATS.Net**: Meta package that includes all other packages except extensions
- **NATS.Client.Core**: [Core NATS](https://docs.nats.io/nats-concepts/core-nats)
- **NATS.Client.JetStream**: [JetStream](https://docs.nats.io/nats-concepts/jetstream)
- **NATS.Client.KeyValueStore**: [Key/Value Store](https://docs.nats.io/nats-concepts/jetstream/key-value-store)
- **NATS.Client.ObjectStore**: [Object Store](https://docs.nats.io/nats-concepts/jetstream/obj_store)
- **NATS.Client.Services**: [Services](https://docs.nats.io/using-nats/developer/services)
- **NATS.Client.Simplified**: simplify common use cases especially for beginners
- **NATS.Client.Serializers.Json**: JSON serializer for ad-hoc types
- **NATS.Extensions.Microsoft.DependencyInjection**: extension to configure DI container

## Client and Orbit

NATS client functionality is split across two layers: the **core client**
(`NATS.Net`, this repo) and **[Orbit](https://github.com/synadia-io/orbit.net)**,
a separate set of packages with higher-level utilities.

The split exists so the core can stay small, stable, and consistent across
NATS clients in every language, while Orbit can iterate quickly on
opinionated abstractions without dragging the core API along for the ride.

### Core client (`NATS.Net`)

- Direct API over Core NATS and JetStream as exposed by `nats-server`.
- Lightweight, unopinionated, performance-oriented.
- API surface kept in **parity** with other official NATS clients
  (Rust, Go, Java, JS, Python, C). A feature shipped here should look
  the same shape everywhere.
- Stable, conservative versioning. Breaking changes are rare and deliberate.

### Orbit (`orbit.net`)

- Higher-level, opinionated abstractions built **on top of** the core client.
- Per-package versioning, so an experimental utility can iterate
  without bumping every other piece.
- Free to be language-specific: a .NET-idiomatic API does not need to match
  the equivalent in other languages.
- May lag, omit, or extend cross-client parity items.

### What goes where?

| Concern                                            | Core (`NATS.Net`)   | Orbit |
|----------------------------------------------------|:-------------------:|:-----:|
| Connect, publish, subscribe, request/reply         | ✅                  |       |
| JetStream publish, consumers, streams, KV, OS      | ✅                  |       |
| Service API (request/reply micro-services)         | ✅                  |       |
| Wire-protocol coverage, auth, TLS, reconnection    | ✅                  |       |
| Cross-client parity, conservative semver           | ✅                  |       |
| Opinionated helpers / sugar over core APIs         |                     | ✅    |
| New experimental patterns (e.g. partitioned groups)|                     | ✅    |
| KV codecs, distributed counters, NATS contexts     |                     | ✅    |
| .NET-idiomatic abstractions with no parity mandate |                     | ✅    |
| Per-utility versioning, faster API churn allowed   |                     | ✅    |

> **Rule of thumb:** if it is a thin mapping of something `nats-server`
> already speaks and every official client must expose it, it belongs in
> core. If it is a pattern, helper, or abstraction layered on top, it
> belongs in Orbit.

### Layering

```text
   ┌──────────────────────────────────────────────────────┐
   │  Application code                                    │
   └──────────────┬───────────────────────────┬───────────┘
                  │                           │
                  ▼                           ▼
        ┌───────────────────┐       ┌───────────────────┐
        │ Orbit packages    │  uses │ NATS.Net (core)   │
        │ (opinionated,     │──────▶│ (parity, stable,  │
        │  per-pkg semver)  │       │  protocol-level)  │
        └───────────────────┘       └─────────┬─────────┘
                                              │
                                              ▼
                                       ┌─────────────┐
                                       │ nats-server │
                                       └─────────────┘
```

## Contributing

You are welcome to contribute to this project. Here are some steps to get you started:

### Reporting Bugs and Feature Requests

You can report bugs and request features
by opening an [issue on GitHub](https://github.com/nats-io/nats.net/issues/new/choose).

### Join the Community

You can join the community asking questions, sharing ideas, and helping others:

- Join the [NATS Slack](https://slack.nats.io) and find us on the `#dotnet` channel
- Join the discussion on [GitHub Discussions](https://github.com/nats-io/nats.net/discussions)
- Follow us on X [@nats_io](https://x.com/nats_io)

### Contributing Code

> [!NOTE]
> Please make sure to **sign your commits**. All commits must be signed before a _Pull Request_ can be merged.

- Read the [Contributor Guide](CONTRIBUTING.md)
- Fork the repository and create a branch
- Open `NATS.Net.slnx` solution in Visual Studio, Rider or VS Code (or any other editor of your choice)
- Make changes and write tests
- Run tests against a locally installed NATS server in your PATH
- Note that some tests are still not reliable locally, so CI will run all tests
- For a quick check, run `NATS.Client.Platform.Windows.Tests` which is a subset of tests that should pass on Windows
- You can also locally run `NATS.Client.CoreUnit.Tests` and `NATS.Client.Core2.Tests` which are more stable
- Run `dotnet format` at root directory of project to clear warnings that can be auto-formatted
- Run `dotnet build` at root directory and make sure there are no errors or warnings
- Submit a pull request

Please also check out the [Contributor Guide](CONTRIBUTING.md) and [Code of Conduct](CODE-OF-CONDUCT.md).

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
