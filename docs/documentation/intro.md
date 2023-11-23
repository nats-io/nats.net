# Introduction

NATS.Net is a .NET client for the open source [Connective Technology for Adaptive Edge & Distributed Systems - NATS](https://nats.io/)!
It's build on top of the modern .NET platform, taking advantage of all the high performance features and
asynchronous programming model.

NATS.Net, just like NATS, is open source as is this documentation.
Please [let us know](https://natsio.slack.com/channels/dotnet) if you have updates or suggestions for
these docs. You can also create a Pull Request in GitHub using the _Edit this page_ link on each page.

## Quick Start

You can [download the latest](https://nats.io/download/) `nats-server` for your platform and run it without any arguments.

```shell
$ nats-server
```

`nats-server` will listen on its default TCP port 4222. By default `nats-server` will not support persistence and only
provide the foundational messaging features also called [Core NATS](https://docs.nats.io/nats-concepts/core-nats). You can
also enable [JetStream](https://docs.nats.io/nats-concepts/jetstream) by passing the `-js` flag to `nats-server` and enable
persistence and other advanced features.

If you prefer using containers, you can also run the latest
[NATS server image](https://docs.nats.io/running-a-nats-service/nats_docker) using Docker:

```shell
$ docker run nats
```

Here are some quick examples to get you started with NATS.Net:

# [Core NATS](#tab/core-nats)

Core NATS is the basic messaging functionality. Messages can be published to a subject and received by one or more
subscribers listening to the same subject only when they are running. There is no persistence and messages are
not stored anywhere.

Start NATS server with default options:

```shell
$ nats-server
```

Reference [NATS.Net](https://www.nuget.org/packages/NATS.Net) NuGet package in your project:

[!code-csharp[](../../tests/NATS.Net.DocsExamples/IntroPage.cs#core-nats)]

# [JetStream](#tab/jetstream)

JetStream is the distributed persistence system built-in to the same NATS server binary. Messages published
to JetStream are stored on the NATS JetStream server and can be retrieved by consumers any time after publishing.

Start NATS server with JetStream enabled:

```shell
$ nats-server -js
```

Reference [NATS.Net NuGet package](https://www.nuget.org/packages/NATS.Net/) in your project:

[!code-csharp[](../../tests/NATS.Net.DocsExamples/IntroPage.cs#jetstream)]

---

> [!NOTE]
> Every [`NatsConnection`](xref:NATS.Client.Core.NatsConnection) instance is a TCP connection to a NATS server.
> Typically an application will only need one
> connection and many subscriptions and publishers would share that same connection. Connections are relatively
> heavyweight and expensive to create while
> subscriptions and publishers are lightweight internal NATS protocol handlers.
> NATS.Net should be able to handle large numbers of subscriptions
> and publishers per connection.

Now you should be able to run NATS server on your machine and use the above code samples to see the basics of
NATS messaging and persistence.

## What's Next

[Core NATS](core/intro.md) is the base set of functionalities and qualities of service offered by a NATS service infrastructure.

[JetStream](jetstream/intro.md) is the distributed persistence system built-in to the same NATS server binary.

[Key/Value Store](key-value-store/intro.md) is the built-in distributed persistent associative arrays built on top of JetStream.

[Object Store](object-store/intro.md) is the built-in distributed persistent objects of arbitrary size built on top of JetStream.

[Services](services/intro.md) is the services protocol built on top of core NATS enabling discovery and monitoring of services you develop.
