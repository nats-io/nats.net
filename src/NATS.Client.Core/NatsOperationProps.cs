namespace NATS.Client.Core;

public record NatsOperationProps
{
    public NatsOperationProps(string subject, string inboxPrefix)
        : this(new NatsSubject(subject, inboxPrefix))
    {
    }

    public NatsOperationProps(string subjectTemplate, string subjectId, string inboxPrefix)
        : this(new NatsSubject(subjectTemplate, "SubjectId", subjectId, inboxPrefix))
    {
    }

    public NatsOperationProps(NatsSubject subject)
    {
        Subject = subject;
    }

    public NatsSubject Subject { get; private set; }
}
