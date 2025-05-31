namespace NATS.Client.Core;

public record NatsProcessProps : NatsOperationProps
{
    public NatsProcessProps(string subject, int subscriptionId)
        : base(subject) => SubscriptionId = subscriptionId;

    public long InboxId { get; internal set; }

    // check if the subject is a reply to a request
    // by checking if the subject length is less than two NUIDs + dots
    // e.g. _INBOX.Hu5HPpWesrJhvQq2NG3YJ6.Hu5HPpWesrJhvQq2NG3YLw
    //  vs. _INBOX.Hu5HPpWesrJhvQq2NG3YJ6.1234
    // otherwise, it's not a reply in direct mode.
    public bool IsDirectReply => InboxPrefix != null && Subject.Length < InboxPrefix.Length + 1 + 22 + 1 + 22;

    public int SubscriptionId { get; set; }

    public NatsSubscriptionProps? Subscription { get; internal set; } = null;

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
