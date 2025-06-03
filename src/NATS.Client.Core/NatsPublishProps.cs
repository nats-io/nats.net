namespace NATS.Client.Core;

public record NatsPublishProps : NatsMessagingProps
{
    public NatsPublishProps(string subject)
        : base(subject)
    {
    }

    public NatsPublishProps(string subjectTemplate, string subjectId)
        : base(subjectTemplate, subjectId)
    {
    }
}
