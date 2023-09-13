# Managing JetStream and the JetStream Context

_JetStream Context_ is a NATS JetStream Client concept which is mainly responsible for managing streams. It serves as
the entry point for creating, configuring, and controlling the streams. JetStream Context also exposes methods to
manage consumers directly, bypassing the need to get or create a stream first.

You can create a context using an existing NATS connection:

```csharp
await using var nats = new NatsConnection();

var js = new NatsJSContext(nats);
```

## Streams

Streams are _message stores_, each stream defines how messages are stored and what the limits (duration, size, interest)
of the retention are. Streams consume normal NATS subjects, any message published on those subjects will be captured in
the defined storage system. You can do a normal publish to the subject for unacknowledged delivery, though it's better
to use the JetStream publish calls instead as the JetStream server will reply with an acknowledgement that it was
successfully stored.

An example of creating a stream:

```csharp
await js.CreateStreamAsync("orders", subjects: new []{"orders.>"});
```

However, in practice streams are usually managed separately from the applications, for example using the [NATS command
line client](https://github.com/nats-io/natscli) you can create a stream interactively:

```shell
$ nats stream create my_events --subjects 'events.*'
? Storage  [Use arrows to move, type to filter, ? for more help]
> file
  memory
# you can safely choose defaults for testing and development
```

Refer to [NATS JetStream documentation](https://docs.nats.io/nats-concepts/jetstream#functionalities-enabled-by-jetstream)
for stream concepts and more information.

## Consumers

A [consumer](https://docs.nats.io/nats-concepts/jetstream/consumers) is a stateful view of a stream. It acts as
interface for clients to consume a subset of messages stored in a stream and will keep track of which messages were
delivered and acknowledged by clients.

Unlike streams, consumers are accessed by NATS client libraries as part of message consumption:

```csharp
// Create or get a consumer
var consumer = await js.CreateConsumerAsync(stream: "orders", consumer: "order_processor");

// Get an existing consumer
var consumer = await js.GetConsumerAsync(stream: "orders", consumer: "order_processor");
```
