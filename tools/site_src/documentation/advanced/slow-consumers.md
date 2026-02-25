# Slow Consumers

When a subscriber can't keep up with the rate of incoming messages, its internal bounded channel fills up.
By default, new messages are dropped when this happens. This is aligned with NATS at-most-once delivery.

## Detecting Dropped Messages

The `MessageDropped` event fires every time a message is dropped from a subscription's internal channel:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SlowConsumerPage.cs#message-dropped)]

## Detecting Slow Consumers

The `SlowConsumerDetected` event (added in v2.7.2) fires once per slow consumer episode.
It resets after the subscription catches up (channel drains), so it will fire again if the subscription falls behind a second time:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SlowConsumerPage.cs#slow-consumer-detected)]

## Tuning Channel Capacity

Each subscription has an internal bounded channel with a default capacity of 1024 messages.
You can tune this globally or per subscription:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SlowConsumerPage.cs#tuning)]

## Channel Full Mode

The `SubPendingChannelFullMode` option controls what happens when the channel is full:

- `BoundedChannelFullMode.DropNewest` (default for `NatsConnection`) - newest messages are dropped
- `BoundedChannelFullMode.Wait` (default for `NatsClient`) - backpressure is applied, but the server may disconnect you as a slow consumer

> [!WARNING]
> Using `BoundedChannelFullMode.Wait` prevents message loss at the client level, but if the subscriber
> can't keep up, the NATS server itself may disconnect the client as a slow consumer.

## Suppressing Warnings

By default, a warning is logged once per slow consumer episode. You can suppress these logs while still
receiving the events:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SlowConsumerPage.cs#suppress-warnings)]
