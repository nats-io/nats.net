# Publishing Messages to Streams

If you want to persist your messages you can do a normal publish to the subject for unacknowledged delivery, though
it's better to use the JetStream context publish calls instead as the JetStream server will reply with an acknowledgement
that it was successfully stored.

The subject must be configured on a stream to be persisted:

```csharp
await using var nats = new NatsConnection();
var js = new NatsJSContext(nats);

await js.CreateStreamAsync("orders", subjects: new []{"orders.>"});
```

or using the nats cli:

```shell
$ nats stream create orders --subjects 'orders.>'
```

Then you can publish to subjects captured by the stream:

```csharp
await using var nats = new NatsConnection();
var js = new NatsJSContext(nats);

var order = new Order { OrderId = 1 };

var ack = await js.PublishAsync("orders.new.1", order);

ack.EnsureSuccess();

public record Order(int OrderId);
```

## Message Deduplication

JetStream support
[idempotent message writes](https://docs.nats.io/using-nats/developer/develop_jetstream/model_deep_dive#message-deduplication)
by ignoring duplicate messages as indicated by the message ID. Message ID is not part of the message but rather passed
as metadata, part of the message headers.

```csharp
var ack = await js.PublishAsync("orders.new.1", order, msgId: "1");
if (ack.Duplicate)
{
    // A message with the same ID was published before
}
```
