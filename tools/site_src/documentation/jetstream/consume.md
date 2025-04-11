# Consuming Messages from Streams

Consuming messages from a stream can be done using one of three different methods depending on your application needs.
You can access these methods from the consumer object created using JetStream context:

Install [NATS.Net](https://www.nuget.org/packages/NATS.Net) from Nuget.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ConsumePage.cs#js)]

## Next Method

Next method is the simplest way of retrieving messages from a stream. Every time you call the next method, you get
a single message or nothing based on the expiry time to wait for a message. Once a message is received you can
process it and call next again for another.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ConsumePage.cs#consumer-next)]

Next is the simplest and most conservative way of consuming messages since you request a single message from JetStream
server then acknowledge it before requesting more. However, next method is also the least performant since
there is no message batching.

## Fetch Method

Fetch method requests messages in batches to improve the performance while giving the application control over how
fast it can process messages without overwhelming the application process.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ConsumePage.cs#consumer-fetch)]

## Consume Method

Consume method is the most performant method of consuming messages. Requests for messages (a.k.a. pull requests) are
overlapped so that there is a constant flow of messages from the JetStream server. Flow is controlled by `MaxMsgs`
or `MaxBytes` and respective thresholds to not overwhelm the application and to not waste server resources.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ConsumePage.cs#consumer-consume)]

## Handling Exceptions

While consuming messages (using next, fetch or consume methods) there are several scenarios where exceptions might be
thrown by the client library, for example:

* Consumer is deleted by another application or operator
* Connection to NATS server is interrupted (mainly for next and fetch methods, consume method can recover)
* Client pull request is invalid
* Account permissions have changed
* Cluster leader changed

A naive implementation might try to recover from errors assuming they are temporary e.g. the stream or the consumer
will be created eventually:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/JetStream/ConsumePage.cs#consumer-consume-error)]

Depending on your application you should configure streams and consumers with appropriate settings so that the
messages are processed and stored based on your requirements.
