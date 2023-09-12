# JetStream

[JetStream](https://docs.nats.io/nats-concepts/jetstream) is the built-in distributed persistence system which enables
new functionalities and higher qualities of service on top of the base _Core NATS_ functionalities and qualities of service.

JetStream is built-in to nats-server and you only need 1 (or 3 or 5 if you want fault-tolerance against 1 or 2
simultaneous NATS server failures) of your NATS server(s) to be JetStream enabled for it to be available to all the
client applications.

JetStream can be enabled by running the server with `-js` flag e.g. `nats-server -js`.

## Streaming: temporal decoupling between the publishers and subscribers

One of the tenets of basic publish/subscribe messaging is that there is a required temporal coupling between the
publishers and the subscribers: subscribers only receive the messages that are published when they are actively
connected to the messaging system (i.e. they do not receive messages that are published while they are not subscribing
or not running or disconnected).

Streams capture and store messages published on one (or more) subject and allow client applications to create
consumers at any time to 'replay' (or consume) all or some of the messages stored in the stream.

Streams are message stores, each stream defines how messages are stored and what the limits (duration, size, interest)
of the retention are. Streams consume normal NATS subjects, any message published on those subjects will be captured
in the defined storage system.

A consumer is a stateful view of a stream. It acts as interface for clients to consume a subset of messages stored in a
stream and will keep track of which messages were delivered and acknowledged by clients.

## Quick Start

[Download the latest](https://nats.io/download/) `nats-server` for your platform and run it with JetStream enabled:

```shell
$ nats-server -js
```

NATS server will listen on its default TCP port 4222.

Then create a .NET 6 (or above) console project, add `NATS.Client.JetStream` preview NuGet package and paste the below
code snippet into `Program.cs`:

```csharp
using System;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

var opts = NatsOpts.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

await using var nc = new NatsConnection(opts);
var js = new NatsJSContext(nc);

await js.CreateStreamAsync("orders", subjects: new []{"orders.>"});

for (var i = 0; i < 10; i++)
{
    var ack = await js.PublishAsync($"orders.new.{i}", new Order(i));
    ack.EnsureSuccess();
}

var consumer = await js.CreateConsumerAsync("orders", "order_processor");

Console.WriteLine($"Consume...");
await foreach (var msg in consumer.ConsumeAllAsync<Order>())
{
    var order = msg.Data;
    Console.WriteLine($"Processing {msg.Subject} {order}...");
    await msg.AckAsync();
    if (order.OrderId == 5)
        break;
}
Console.WriteLine($"Done consuming.");

public record Order(int OrderId);
```

[Managing JetStream](manage.md) covers how to create, update, get, list and delete streams and consumers.

[Publishing messages to streams](publish.md) is achieved by simply publishing to a subject where a stream is configured
to be interested in that subject.

[Consuming messages from streams](consume.md) explains different ways of retrieving persisted messages.
