namespace NATS.Client.Core;

public readonly record struct NatsStats
(
    long SentBytes,
    long ReceivedBytes,
    long PendingMessages,
    long SentMessages,
    long ReceivedMessages,
    long SubscriptionCount
);

internal sealed class ConnectionStatsCounter
{
    // for operate Interlocked.Increment/Decrement/Add, expose field as public
    public long SentBytes;
    public long SentMessages;
    public long PendingMessages;
    public long ReceivedBytes;
    public long ReceivedMessages;
    public long SubscriptionCount;

    public NatsStats ToStats()
    {
        return new NatsStats(SentBytes, ReceivedBytes, PendingMessages, SentMessages, ReceivedMessages, SubscriptionCount);
    }
}
