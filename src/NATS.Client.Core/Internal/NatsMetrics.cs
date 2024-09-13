using System.Diagnostics.Metrics;

namespace NATS.Client.Core.Internal;

public sealed class NatsMetrics
{
    public const string MeterName = "NATS.Client";

    private readonly Meter _meter;

    private readonly Counter<long> _subscriptionCounter;
    private readonly Counter<long> _sentBytesCounter;
    private readonly Counter<long> _receivedBytesCounter;
    private readonly Counter<long> _pendingMessagesCounter;
    private readonly Counter<long> _sentMessagesCounter;
    private readonly Counter<long> _receivedMessagesCounter;

    public NatsMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _subscriptionCounter = _meter.CreateCounter<long>(
            "nats.client.subscription.count",
            unit: "{subscriptions}",
            description: "Number of subscriptions");

        _sentBytesCounter = _meter.CreateCounter<long>(
            "nats.client.sent.bytes",
            unit: "bytes",
            description: "Number of bytes sent");

        _receivedBytesCounter = _meter.CreateCounter<long>(
            "nats.client.received.bytes",
            unit: "bytes",
            description: "Number of bytes received");

        _pendingMessagesCounter = _meter.CreateCounter<long>(
            "nats.client.pending.messages",
            unit: "messages",
            description: "Number of pending messages");

        _sentMessagesCounter = _meter.CreateCounter<long>(
            "nats.client.sent.messages",
            unit: "messages",
            description: "Number of messages sent");

        _receivedMessagesCounter = _meter.CreateCounter<long>(
            "nats.client.received.messages",
            unit: "messages",
            description: "Number of messages received");
    }

    public void IncrementSubscriptionCount() => _subscriptionCounter.Add(1);

    public void DecrementSubscriptionCount() => _subscriptionCounter.Add(-1);

    public void AddSentBytes(long bytes) => _sentBytesCounter.Add(bytes);

    public void AddReceivedBytes(long bytes) => _receivedBytesCounter.Add(bytes);

    public void AddPendingMessages(long messages) => _pendingMessagesCounter.Add(messages);

    public void AddSentMessages(long messages) => _sentMessagesCounter.Add(messages);

    public void AddReceivedMessages(long messages) => _receivedMessagesCounter.Add(messages);

    // This factory used when type is created without DI.
    internal sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose() { }
    }
}
