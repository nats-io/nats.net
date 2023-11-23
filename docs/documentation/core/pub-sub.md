# Publish-Subscribe Pattern

NATS implements a publish-subscribe message distribution model for one-to-many communication.
A publisher sends a message on a subject and any active subscriber listening on that subject
receives the message.

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Core/PubSubPage.cs#pubsub)]

## Subscriptions with Lower Level Control

The
[`SubscribeAsync()`](xref:NATS.Client.Core.INatsConnection.SubscribeAsync``1(System.String,System.String,NATS.Client.Core.INatsDeserialize{``0},NATS.Client.Core.NatsSubOpts,System.Threading.CancellationToken))
method is a convenient way to subscribe to a subject and receive messages without much effort.
If you need more control over how subscription is handled, you can use the
[`SubscribeCoreAsync()`](xref:NATS.Client.Core.INatsConnection.SubscribeCoreAsync``1(System.String,System.String,NATS.Client.Core.INatsDeserialize{``0},NATS.Client.Core.NatsSubOpts,System.Threading.CancellationToken))
method instead.

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Core/PubSubPage.cs#lowlevel)]

> [!NOTE]
> [`PingAsync()`](xref:NATS.Client.Core.INatsConnection.PingAsync(System.Threading.CancellationToken)) is somewhat a
> special method in all NATS clients. It is used to send a ping to the server and
> receive a pong back while measuring the round trip time. Since it waits for the server to respond, as a side effect
> it also flushes the outgoing buffer. This is why we call it before publishing any messages in the example above,
> making sure the subscription request is received by the server before any publish requests.
>
> Remember that every [`NatsConnection`](xref:NATS.Client.Core.NatsConnection) instance is a single TCP connection
> and all the calls sent to the server are
> essentially serialized back to back after they're picked up from internal queues and buffers.
