namespace NATS.Client.Core;

public record NatsPublishProps : NatsMessagingProps
{
    public NatsPublishProps(string subject, string inboxPrefix = "UNKNOWN")
        : base(subject, inboxPrefix)
    {
    }

    public NatsPublishProps(string subjectTemplate, string subjectId, string inboxPrefix = "UNKNOWN")
        : base(subjectTemplate, subjectId, inboxPrefix)
    {
    }

    internal NatsPublishProps(string subjectTemplate, Dictionary<string, object> properties, string inboxPrefix = "UNKNOWN")
        : base(subjectTemplate, properties, inboxPrefix)
    {
    }
}
