namespace NATS.Client.Core;

public record NatsMessagingProps : NatsOperationProps
{
    internal NatsMessagingProps(string subject)
        : base(subject)
    {
    }

    public string? ReplyTo { get; private set; } = null;

    internal int PayloadLength => TotalMessageLength - HeaderLength;

    internal int HeaderLength { get; set; }

    internal int TotalMessageLength { get; set; }

    internal int FramingLength { get; set; }

    internal int TotalEnvelopeLength => TotalMessageLength + FramingLength;

    public void SetReplyTo(string? replyTo)
    {
        if (replyTo != null)
        {
            ReplyTo = replyTo;
        }
    }
}
