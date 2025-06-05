namespace NATS.Client.Core;

public record NatsMessagingProps : NatsOperationProps
{
    internal NatsMessagingProps(string subject)
        : base(subject)
    {
    }

    internal NatsMessagingProps(string subjectTemplate, string subjectId)
        : base(subjectTemplate, subjectId)
    {
    }

    internal NatsMessagingProps(string subjectTemplate, Dictionary<string, object> properties)
        : base(subjectTemplate, properties)
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
        SetReplyTo(new NatsSubject(replyToTemplate, "ReplyToId", replyToId));
    }

    public void SetReplyTo(string? replyTo)
    {
        if (replyTo != null)
        {
            SetReplyTo(new NatsSubject(replyTo));
        }
    }
}
