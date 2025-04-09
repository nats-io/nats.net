# Managing JetStream and the JetStream Context

_JetStream Context_ is a NATS JetStream Client concept which is mainly responsible for managing streams. It serves as
the entry point for creating, configuring, and controlling the streams. JetStream Context also exposes methods to
manage consumers directly, bypassing the need to get or create a stream first.

You can create a context using an existing NATS connection:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ManagingPage.cs#js)]

## Streams

Streams are _message stores_, each stream defines how messages are stored and what the limits (duration, size, interest)
of the retention are. Streams consume normal NATS subjects, any message published on those subjects will be captured in
the defined storage system. You can do a normal publish to the subject for unacknowledged delivery, though it's better
to use the JetStream publish calls instead as the JetStream server will reply with an acknowledgement that it was
successfully stored.

An example of creating a stream:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ManagingPage.cs#stream)]

However, in practice streams are usually managed separately from the applications, for example using the [NATS command
line client](https://github.com/nats-io/natscli) you can create a stream interactively:

```shell
$ nats stream create ORDERS --subjects 'orders.>'
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

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ManagingPage.cs#consumer-create)]
[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ManagingPage.cs#consumer-get)]

### Ephemeral and Durable Consumers

A consumer can be ephemeral or durable. It is considered durable when an explicit name is set on the
DurableName field when creating the consumer, otherwise it is considered ephemeral. Durables and ephemeral behave
exactly the same except that an ephemeral will be automatically cleaned up (deleted) after a period of inactivity,
specifically when there are no subscriptions bound to the consumer.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ManagingPage.cs#consumer-durable)]
[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ManagingPage.cs#consumer-ephemeral)]
