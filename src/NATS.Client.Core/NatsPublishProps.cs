namespace NATS.Client.Core;

public record NatsPublishProps : NatsOperationProps
{
    public NatsPublishProps(string subject)
        : base(subject)
    {
    }

    public NatsPublishProps(string subjectTemplate, string subjectId)
        : base(subjectTemplate, subjectId)
    {
    }

    public int PayloadLength => TotalMessageLength - HeaderLength;

    public int HeaderLength { get; internal set; }

    public int TotalMessageLength { get; internal set; }

    public int MetaLength { get; internal set; }

    public int TotalEnvelopeLength => TotalMessageLength + MetaLength;

    public string? ReplyToId { get; private set; } = null;

    public string? ReplyToTemplate { get; private set; }

    public string? ReplyTo => ReplyToTemplate == null || ReplyToId == null ?
                ReplyToTemplate :
                ReplyToTemplate.Replace("{{ReplyToId}}", ReplyToId);

    public void SetReplyTo(string? replyToTemplate, string? replyToId = default)
    {
        ReplyToTemplate = replyToTemplate;
        if (ReplyToTemplate != null && ReplyToTemplate.Contains("{{ReplyToId}}"))
        {
            ReplyToId = replyToId;
        }
        else
        {
            ReplyToId = null;
        }
    }
}
