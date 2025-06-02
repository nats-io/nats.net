namespace NATS.Client.Core;

public record NatsPublishProps : NatsMessagingProps
{
    public NatsPublishProps(string subject, string inboxPrefix)
        : base(subject, inboxPrefix)
    {
    }

    public NatsPublishProps(string subjectTemplate, string subjectId, string inboxPrefix)
        : base(subjectTemplate, subjectId, inboxPrefix)
    {
    }
}
