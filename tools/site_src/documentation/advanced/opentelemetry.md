# OpenTelemetry

NATS.Net has built-in distributed tracing support using [`System.Diagnostics.Activity`](https://learn.microsoft.com/dotnet/api/system.diagnostics.activity),
the standard .NET API for OpenTelemetry. Activities are created automatically for publish and subscribe operations,
and trace context is propagated through message headers so that send and receive spans are linked across services.

The activity source name is `NATS.Net`.

## Setting Up Tracing

To collect traces, register a listener for the `NATS.Net` activity source. You can use the OpenTelemetry SDK
with an exporter (Jaeger, Zipkin, OTLP, etc.) or a plain `ActivityListener` for lightweight scenarios:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/OpenTelemetryPage.cs#setup)]

## Automatic Trace Context Propagation

When you publish a message, the client injects the current trace context into the message headers.
When a subscriber reads the message, the receive activity is automatically parented to the send activity,
giving you end-to-end traces across services with no extra code:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/OpenTelemetryPage.cs#publish-subscribe)]

## Starting Custom Activities

You can start child activities under a message's trace context using the `StartActivity` extension method.
This is useful for tracking processing work that happens after a message is received:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/OpenTelemetryPage.cs#custom-activity)]

The `StartActivity` method is available on both [`NatsMsg<T>`](xref:NATS.Client.Core.NatsMsgTelemetryExtensions)
and [`INatsJSMsg<T>`](xref:NATS.Client.JetStream.NatsJSTelemetryExtensions) for JetStream messages.

## Filtering

Use [`NatsInstrumentationOptions.Default.Filter`](xref:NATS.Client.Core.NatsInstrumentationOptions) to skip
telemetry for specific requests. When the filter returns `false`, no activity is created:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/OpenTelemetryPage.cs#filter)]

## Enriching Activities

Use [`NatsInstrumentationOptions.Default.Enrich`](xref:NATS.Client.Core.NatsInstrumentationOptions) to add
custom tags to every activity:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/OpenTelemetryPage.cs#enrich)]

## Semantic Conventions

NATS.Net follows the [OpenTelemetry Semantic Conventions for Messaging](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/).
The following attributes are set on activities:

| Attribute | Example | Description |
|---|---|---|
| `messaging.system` | `nats` | Always `nats` |
| `messaging.operation` | `publish` / `receive` | Operation type |
| `messaging.destination.name` | `orders.new` | Subject name |
| `messaging.client_id` | `42` | NATS client ID |
| `server.address` | `localhost` | Server host |
| `server.port` | `4222` | Server port |
| `network.protocol.name` | `nats` | Protocol name |
| `network.transport` | `tcp` | Transport protocol |
| `network.peer.address` | `localhost` | Remote host |
| `network.peer.port` | `4222` | Remote port |
| `network.local.address` | `127.0.0.1` | Local IP |

Receive activities include additional attributes:

| Attribute | Example | Description |
|---|---|---|
| `messaging.destination.template` | `orders.*` | Subscription subject pattern |
| `messaging.message.body.size` | `1024` | Message body size in bytes |
| `messaging.message.envelope.size` | `1280` | Total message size in bytes |
| `messaging.consumer.group.name` | `workers` | Queue group (if used) |
