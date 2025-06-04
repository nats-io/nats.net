namespace NATS.Client.Core;

public record NatsMessagingProps : NatsOperationProps
{
    internal NatsMessagingProps(string subject, string inboxPrefix)
        : base(subject, inboxPrefix)
    {
    }

    internal NatsMessagingProps(string subjectTemplate, string subjectId, string inboxPrefix)
        : base(subjectTemplate, subjectId, inboxPrefix)
    {
    }

    internal NatsMessagingProps(string subjectTemplate, Dictionary<string, object> properties, string inboxPrefix)
        : base(subjectTemplate, properties, inboxPrefix)
    {
    }

    public NatsSubject? ReplyTo { get; private set; } = null;

    internal int PayloadLength => TotalMessageLength - HeaderLength;

    internal int HeaderLength { get; set; }

    internal int TotalMessageLength { get; set; }

    internal int FramingLength { get; set; }

    internal int TotalEnvelopeLength => TotalMessageLength + FramingLength;

    public void SetReplyTo(string replyToTemplate, object replyToId)
    {
        ReplyTo = new NatsSubject(replyToTemplate, "ReplyToId", replyToId, Subject.InboxPrefix);
    }

    public void SetReplyTo(string? replyTo)
    {
        if (replyTo != null)
        {
            ReplyTo = new NatsSubject(replyTo);
        }
    }
}
