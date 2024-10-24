using System.Diagnostics.Metrics;

namespace NATS.Client.Core.Internal;

public class NatsMetrics
{
    public const string MeterName = "NATS.Client";
    public const string PendingMessagesInstrumentName = $"{InstrumentPrefix}.pending.messages";
    public const string SentBytesInstrumentName = $"{InstrumentPrefix}.sent.bytes";
    public const string ReceivedBytesInstrumentName = $"{InstrumentPrefix}.received.bytes";
    public const string SentMessagesInstrumentName = $"{InstrumentPrefix}.sent.messages";
    public const string ReceivedMessagesInstrumentName = $"{InstrumentPrefix}.received.messages";
    public const string SubscriptionInstrumentName = $"{InstrumentPrefix}.subscription.count";

    private const string InstrumentPrefix = "nats.client";

    private static readonly Meter _meter;
    private static readonly Counter<long> _subscriptionCounter;
    private static readonly Counter<long> _pendingMessagesCounter;
    private static readonly Counter<long> _sentBytesCounter;
    private static readonly Counter<long> _receivedBytesCounter;
    private static readonly Counter<long> _sentMessagesCounter;
    private static readonly Counter<long> _receivedMessagesCounter;

    static NatsMetrics()
    {
        _meter = new Meter(MeterName);

        _subscriptionCounter = _meter.CreateCounter<long>(
            SubscriptionInstrumentName,
            unit: "{subscriptions}",
            description: "Number of subscriptions");

        _pendingMessagesCounter = _meter.CreateCounter<long>(
            PendingMessagesInstrumentName,
            unit: "{messages}",
            description: "Number of pending messages");

        _sentBytesCounter = _meter.CreateCounter<long>(
            SentBytesInstrumentName,
            unit: "{bytes}",
            description: "Number of bytes sent");

        _receivedBytesCounter = _meter.CreateCounter<long>(
            ReceivedBytesInstrumentName,
            unit: "{bytes}",
            description: "Number of bytes received");

        _sentMessagesCounter = _meter.CreateCounter<long>(
            SentMessagesInstrumentName,
            unit: "{messages}",
            description: "Number of messages sent");

        _receivedMessagesCounter = _meter.CreateCounter<long>(
            ReceivedMessagesInstrumentName,
            unit: "{messages}",
            description: "Number of messages received");
    }

    public static void IncrementSubscriptionCount()
    {
        _subscriptionCounter.Add(1);
    }

    public static void DecrementSubscriptionCount()
    {
        _subscriptionCounter.Add(-1);
    }

    public static void AddPendingMessages(long messages)
    {
        _pendingMessagesCounter.Add(messages);
    }

    public static void AddSentBytes(long bytes)
    {
        _sentBytesCounter.Add(bytes);
    }

    public static void AddReceivedBytes(long bytes)
    {
        _receivedBytesCounter.Add(bytes);
    }

    public static void AddSentMessages(long messages)
    {
        _sentMessagesCounter.Add(messages);
    }

    public static void AddReceivedMessages(long messages)
    {
        _receivedMessagesCounter.Add(messages);
    }
}
