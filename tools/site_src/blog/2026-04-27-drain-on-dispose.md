---
title: Opt-in drain on dispose for JetStream consumers
date: 2026-04-27
---

# Opt-in drain on dispose for JetStream consumers

_2026-04-27_

Disposing a `NatsConnection` while a JetStream `Consume`, `Fetch`, or
ordered-consumer loop was still buffering messages used to drop pending
acks. The socket got closed before the command writer flushed, so the
`UNSUB` and any acks the user loop had queued went nowhere. The messages
stayed `NumAckPending` on the server until `AckWait` expired and were
then redelivered.

There are now two opt-in options on `NatsOpts` to control this:

- `DrainSubscriptionsOnDispose` (default `false`). On dispose, drain
  subscriptions and flush the command writer before closing the socket.
- `ConsumerDrainOnDisposeTimeout` (default `null`). When set, JetStream
  consumer and fetch dispose waits up to this timeout for the user's
  consume loop to finish acking the buffered messages before returning.
  Only effective when `DrainSubscriptionsOnDispose` is also `true`.

```csharp
var opts = NatsOpts.Default with
{
    DrainSubscriptionsOnDispose = true,
    ConsumerDrainOnDisposeTimeout = TimeSpan.FromSeconds(5),
};

await using var nats = new NatsConnection(opts);
```

Both default to off, so existing apps see no behavior change. Turn them
on if you want a clean shutdown without redeliveries.

## A note on resilience

You do not lose messages either way. JetStream redelivers anything that
wasn't acked once `AckWait` expires, which is the whole point of
at-least-once delivery. The old default is still correct, just noisier:
a graceful shutdown can cause redeliveries you didn't actually need.
For most apps that's fine. These options are for when you want shutdown
to be precise rather than just safe.

Applies to `Consume`, `Fetch`, and ordered consumer loops in
`NATS.Client.JetStream`. Lands in `2.8.0`.
