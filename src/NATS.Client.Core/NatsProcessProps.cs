namespace NATS.Client.Core;

public record NatsProcessProps : NatsMessagingProps
{
    private long _subjectValue = 0;

    internal NatsProcessProps(string subject, int subscriptionId)
        : base(subject) => SubscriptionId = subscriptionId;

    public long InboxId { get; internal set; }

    public int SubscriptionId { get; internal set; }

    public NatsSubscriptionProps? Subscription { get; internal set; } = null;

    // check if the subject is a reply to a request
    // by checking if the subject length is less than two NUIDs + dots
    // e.g. _INBOX.Hu5HPpWesrJhvQq2NG3YJ6.Hu5HPpWesrJhvQq2NG3YLw
    //  vs. _INBOX.Hu5HPpWesrJhvQq2NG3YJ6.1234
    // otherwise, it's not a reply in direct mode.
    internal bool IsDirectReply => Subject.Length < InboxPrefix.Length + 1 + 22 + 1 + 22;

    internal long SubjectNumber
    {
        get
        {
            if (UsesInbox && _subjectValue == 0)
            {
                var idString = Subject.AsSpan().Slice(InboxPrefix.Length + 1)
#if NETSTANDARD2_0
                    .ToString()
#endif
                    ;

                if (long.TryParse(idString, out var id))
                {
                    _subjectValue = id;
                }
            }

            return _subjectValue;
        }
    }
}
