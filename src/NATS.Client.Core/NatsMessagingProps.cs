namespace NATS.Client.Core;

public record NatsMessagingProps : NatsOperationProps
{
    private const string ReplyToIdentifier = "{{ReplyToId}}";

    internal NatsMessagingProps(string subject)
        : base(subject)
    {
    }

    internal NatsMessagingProps(string subjectTemplate, string subjectId)
        : base(subjectTemplate, subjectId)
    {
    }

    public string? ReplyToId { get; private set; } = null;

    public string? ReplyToTemplate { get; private set; }

    public string? ReplyTo => ReplyToTemplate == null || ReplyToId == null ?
                ReplyToTemplate :
                ReplyToTemplate.Replace(ReplyToIdentifier, ReplyToId);

    internal int PayloadLength => TotalMessageLength - HeaderLength;

    internal int HeaderLength { get; set; }

    internal int TotalMessageLength { get; set; }

    internal int MetaLength { get; set; }

    internal int TotalEnvelopeLength => TotalMessageLength + MetaLength;

    public void SetReplyTo(string? replyToTemplate, string? replyToId = default)
    {
        ReplyToTemplate = replyToTemplate;
        if (ReplyToTemplate != null && ReplyToTemplate.Contains(ReplyToIdentifier))
        {
            ReplyToId = replyToId;
        }
        else
        {
            ReplyToId = null;
        }
    }
}
