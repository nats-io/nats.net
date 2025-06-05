namespace NATS.Client.Core;

public record NatsSubscriptionProps : NatsOperationProps
{
    public NatsSubscriptionProps(string subject, string? queueGroup = default)
        : base(subject)
    {
        QueueGroup = queueGroup;
    }

    public NatsSubscriptionProps(int subscriptionId)
        : base(string.Empty)
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; set; }

    public string? QueueGroup { get; internal set; }

    public NatsRequestReplyMode? RequestReplyMode { get; internal set; }
    
    internal bool IsInboxSubject(string inboxPrefix) => !string.IsNullOrEmpty(inboxPrefix)
        && Subject.StartsWith(inboxPrefix, StringComparison.Ordinal);
}
