# Welcome to NATS .NET

NATS .NET is an open source [NATS](https://nats.io) client library for modern [.NET](https://dot.net/). NATS .NET fully supports async programming,
recent APIs like Span, IAsyncEnumerable, Channels and takes advantage of .NET's performance improvements while providing
a pleasant developer experience.

[NATS](https://nats.io) is a lightweight and high-performance messaging system designed for asynchronous communication among
different software components, with modern clustering, security and persistence streaming support
out of the box, in a [single compact binary with no dependencies](https://nats.io/download/), available for any modern
platform, enabling a vast variety of deployment options from edge, IoT, Kubernetes to bare-metal.

NATS .NET brings the power of NATS to the .NET platform, enabling developers to build distributed, cloud native, modern
applications using the tools and languages they already know and love.

> [!NOTE]
> **Don't confuse NuGet packages!**
> NATS .NET package on NuGet is called [NATS.Net](https://www.nuget.org/packages/NATS.Net).
> There is another package called `NATS.Client` which is the older version of the client library
> and will be deprecated eventually.

> [!TIP]
> NATS .NET now supports **.NET Standard** 2.0 and 2.1 along with .NET 6.0 and 8.0,
> which means you can also use it with **.NET Framework** 4.6.2+ and **Unity** 2018.1+.

## Hello, World!

NATS team maintains a demo server you can reach at `demo.nats.io` globally.
You can use this server to quickly write your first NATS .NET application without setting up a server.

> [!NOTE]
> If you're behind a firewall, you might not be able to reach the demo server.
> Check out the [introduction page](documentation/intro.md) for instructions on how to run your own server easily.

Create two console applications, one for subscribing and one for publishing messages.

### The Receiver

```shell
mkdir HelloNats.Receiver
cd HelloNats.Receiver
dotnet new console
dotnet add package NATS.Net
```

[!code-csharp[](../tests/NATS.Net.DocsExamples/IndexPageSub.cs#demo)]

```shell
dotnet run
```

### The Sender

```shell
mkdir HelloNats.Sender
cd HelloNats.Sender
dotnet new console
dotnet add package NATS.Net
```

[!code-csharp[](../tests/NATS.Net.DocsExamples/IndexPagePub.cs#demo)]

```shell
dotnet run
```

Try running the sender from more than one terminal to have some fun. Happy chatting!

The receiver will listen to messages on the `hello.my_room.>` subject and your sender application
will send messages to the matching subjects.
This [subject has a wildcard](https://docs.nats.io/nats-concepts/subjects) `>` at the end, which means it will match
any subject starting with `hello.my_room.`.

## What's Next

[Documentation](documentation/intro.md) can help you start creating your application in no time. Follow our quick start guides.

[API](api/index.md) is the generated reference documentation.
