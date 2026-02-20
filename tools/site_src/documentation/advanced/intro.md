# Advanced Options

For more advanced configuration, you can use the [`NatsOpts`](xref:NATS.Client.Core.NatsOpts)
class to configure the connection to the NATS server.

For example, you can hook your logger to `NatsClient` to make sure all is working as expected or
to get help diagnosing any issues you might have:

(For this example, you need to add [Microsoft.Extensions.Logging.Console](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Console) from Nuget.)

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/IntroPage.cs#logging)]

## NatsClient vs NatsConnection

[`NatsClient`](xref:NATS.Net.NatsClient) is a high-level API that wraps [`NatsConnection`](xref:NATS.Client.Core.NatsConnection)
and provides a more user-friendly interface to interact with the NATS server.
It is the recommended way to interact with the NATS server for beginners.
However, if you need to access the underlying `NatsConnection` instance,
you can do so by using the `Connection` property of `NatsClient` or by creating a new instance of `NatsConnection`.

**So, What's the Difference?**

`NatsClient` implements `INatsClient` and provides a high-level APIs, and also
sets up the serializers to use the expected formats for message types like `int`,
`string`, `byte[]`, and data classes for ad hoc JSON serialization.

`NatsConnection` is the underlying class that manages the connection to the NATS server.
It provides more advanced APIs and allows you to configure the connection in more detail.
`NatsConnection`implements `INatsConnection` which extends `INatsClient` with advanced APIs,
so you can use it as a `NatsClient` instance without any changes to your code. When you
instantiate a `NatsConnection` with default options, you would only have basic serialization
for `int`, `string`, and `byte[]` types, and you would need to set up the serializers for your data classes
if you want to use e.g., JSON serialization.

The other difference is that `NatsClient` sets `SubPendingChannelFullMode` internal channel option to
`BoundedChannelFullMode.Wait` to avoid dropping messages when the subscriber's internal channel is full.
This is a good default for most cases, but you can change it by setting the `SubPendingChannelFullMode` option
in `NatsClient` constructor.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/IntroPage.cs#opts)]

You can also use the `NatsConnection` class directly.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/IntroPage.cs#opts2)]

**Which One Should I Use?**

If you are new to NATS, you should use `NatsClient` as it provides a more user-friendly interface
with sensible defaults especially for serialization.
If you need more control over the connection options, AOT deployments, or custom serializers,
you should use `NatsConnection`.

See also [serialization](serialization.md) for more information on how to set up custom serializers.

> [!NOTE]
> Every [`NatsClient`](xref:NATS.Net.NatsClient) (and the underlying [`NatsConnection`](xref:NATS.Client.Core.NatsConnection))
> instance is a TCP connection to a NATS server.
> Typically an application will only need one
> connection, and many subscriptions and publishers would share that same connection. Connections are relatively
> heavyweight and expensive to create while
> subscriptions and publishers are lightweight internal NATS protocol handlers.
> NATS.Net should be able to handle large numbers of subscriptions
> and publishers per connection.

## Subscriptions with Lower Level Control

The
[`SubscribeAsync()`](xref:NATS.Client.Core.INatsClient.SubscribeAsync``1(System.String,System.String,NATS.Client.Core.INatsDeserialize{``0},NATS.Client.Core.NatsSubOpts,System.Threading.CancellationToken))
method is a convenient way to subscribe to a subject and receive messages without much effort.
If you need more control over how subscription is handled, you can use the
[`SubscribeCoreAsync()`](xref:NATS.Client.Core.INatsConnection.SubscribeCoreAsync``1(System.String,System.String,NATS.Client.Core.INatsDeserialize{``0},NATS.Client.Core.NatsSubOpts,System.Threading.CancellationToken))
method instead.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/IntroPage.cs#lowlevel-sub)]

## Round Trip Time (RTT)

[`PingAsync()`](xref:NATS.Client.Core.INatsClient.PingAsync(System.Threading.CancellationToken)) is somewhat a
special method in all NATS clients, mostly referred to as `rtt`. It is used to send a ping to the server and
receive a pong back while measuring the round trip time. Since it waits for the server to respond, as a side effect,
it also flushes the outgoing buffers.

Remember that every [`NatsConnection`](xref:NATS.Client.Core.NatsConnection) instance is a single TCP connection
and all the calls sent to the server are
essentially sent back to back after they're picked up from internal queues and buffers.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/IntroPage.cs#ping)]

> [!NOTE]
> [`NatsConnection`](xref:NATS.Client.Core.NatsConnection) establishes the first server connection when the first call to subscribe or publish is made.
> You can also call the `ConnectAsync()` method explicitly to establish the connection before any other calls are made.

## What's Next

- [Slow Consumers](slow-consumers.md) explains how to detect and handle subscribers that can't keep up with the message rate, including the `MessageDropped` and `SlowConsumerDetected` events.
- [Serialization](serialization.md) is the process of converting an object into a format that can be stored or transmitted.
- [Security](security.md) is an important aspect of any distributed system. NATS provides a number of security features to help you secure your applications.
- [AOT Deployment](aot.md) is a way to deploy your applications as native platform executables, which produces faster startup times and better performance in most cases.
- [Platform Compatibility](platform-compatibility.md) documents API differences across target frameworks (.NET Standard, .NET 6, .NET 8).
