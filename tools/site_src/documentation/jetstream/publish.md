# Publishing Messages to Streams

If you want to persist your messages you can do a normal publish to the subject for unacknowledged delivery, though
it's better to use the JetStream context publish calls instead as the JetStream server will reply with an acknowledgement
that it was successfully stored.

The subject must be configured on a stream to be persisted:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/PublishPage.cs#js)]
or using the nats cli:

```shell
$ nats stream create ORDERS --subjects 'orders.>'
```

Then you can publish to subjects captured by the stream:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/PublishPage.cs#publish)]

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/IntroPage.cs#serializer)]

## Message Deduplication

JetStream support
[idempotent message writes](https://docs.nats.io/using-nats/developer/develop_jetstream/model_deep_dive#message-deduplication)
by ignoring duplicate messages as indicated by the message ID. Message ID is not part of the message but rather passed
as metadata, part of the message headers.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/PublishPage.cs#publish-duplicate)]
