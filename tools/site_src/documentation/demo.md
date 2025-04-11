# NATS .NET Demo

NATS team maintains a demo server you can reach at `demo.nats.io` globally.
You can use this server to quickly write your first NATS .NET application without setting up a server.

> [!NOTE]
> If you're behind a firewall, you might not be able to reach the demo server.
> Check out the [introduction page](intro.md) for instructions on how to run your own server easily.

Create two console applications, one for subscribing and one for publishing messages.

### The Receiver

```shell
mkdir HelloNats.Receiver
cd HelloNats.Receiver
dotnet new console
dotnet add package NATS.Net
```

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/IndexPageSub.cs#demo)]

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

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/IndexPagePub.cs#demo)]

```shell
dotnet run
```

Try running the sender from more than one terminal to have some fun. Happy chatting!

The receiver will listen to messages on the `hello.my_room.>` subject and your sender application
will send messages to the matching subjects.
This [subject has a wildcard](https://docs.nats.io/nats-concepts/subjects) `>` at the end, which means it will match
any subject starting with `hello.my_room.`.

## What's Next

[Introduction](intro.md) can help you start creating your application in no time. Follow our quick start guides.

[API](../api/index.md) is the generated reference documentation.
