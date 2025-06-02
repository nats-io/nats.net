namespace NATS.Client.Core;

public record NatsSubscriptionProps : NatsOperationProps
{
    private bool unknownPrefix = false;
    public NatsSubscriptionProps(string subject, string? inboxPrefix, string? queueGroup = default)
        : base(subject, inboxPrefix)
    {
        unknownPrefix = false;
        QueueGroup = queueGroup;
    }

    public NatsSubscriptionProps(string subject, string? queueGroup = default)
        : base(subject, "UNKNOWN")
    {
        unknownPrefix = false;
        QueueGroup = queueGroup;
    }

    public NatsSubscriptionProps(int subscriptionId, string? inboxPrefix)
        : base(string.Empty, inboxPrefix)
    {
        SubscriptionId = subscriptionId;
        unknownPrefix = true;
    }

    public NatsSubscriptionProps(int subscriptionId)
        : base(string.Empty, "UNKNOWN")
    {
        unknownPrefix = true;
        SubscriptionId = subscriptionId;
    }

    public NatsSubscriptionProps(string subjectTemplate, string subjectId, string inboxPrefix, string? queueGroup = default)
        : base(subjectTemplate, subjectId, inboxPrefix)
    {
        unknownPrefix = false;
        QueueGroup = queueGroup;
    }

    public NatsSubscriptionProps(NatsSubject subject)
        : base(subject)
    {
    }

    public int SubscriptionId { get; set; }

    public string? QueueGroup { get; internal set; }

    public NatsRequestReplyMode? RequestReplyMode { get; internal set; }
}
