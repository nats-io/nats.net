namespace NATS.Client.Core;

public record NatsSubscriptionProps : NatsOperationProps
{
    public NatsSubscriptionProps(string subject)
        : base(subject)
    {
    }

    public NatsSubscriptionProps(int subscriptionId)
        : base(string.Empty)
    {
        SubscriptionId = subscriptionId;
    }

    public NatsSubscriptionProps(string subjectTemplate, string subjectId)
        : base(subjectTemplate, subjectId)
    {
    }

    public int SubscriptionId { get; set; }

    public string? QueueGroup { get; internal set; }
}
