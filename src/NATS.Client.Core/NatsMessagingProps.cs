namespace NATS.Client.Core;

/// <inheritdoc />
public abstract record NatsMessagingProps : NatsOperationProps
{
    /// <inheritdoc />
    internal NatsMessagingProps(string subject)
        : base(subject)
    {
    }

    /// <summary>
    /// The reply subject that subscribers can use to send a response back to the publisher/requestor.
    /// </summary>
    public string? ReplyTo { get; private set; } = null;

    /// <summary>
    /// The sizing of the payload which has been sent/recieved from a NATS server.
    /// </summary>
    internal int PayloadLength => TotalMessageLength - HeaderLength;

    /// <summary>
    /// The sizing of the headers which has been sent/recieved from a NATS server.
    /// </summary>
    internal int HeaderLength { get; set; }

    /// <summary>
    /// The total size of the data packet which has been generated
    /// </summary>
    /// <remarks>Is the sum of <see cref="HeaderLength"/> and <see cref="PayloadLength"/>.</remarks>
    internal int TotalMessageLength { get; set; }

    /// <summary>
    /// Framing is the first line of a NATS message
    /// </summary>
    /// <remarks>The data in the framing section is system generated</remarks>
    internal int FramingLength { get; set; }

    /// <summary>
    /// The total size of the data packet which has been generated
    /// </summary>
    /// <remarks>Is the sum of <see cref="HeaderLength"/>, <see cref="PayloadLength"/> and <see cref="FramingLength"/> </remarks>
    internal int TotalEnvelopeLength => TotalMessageLength + FramingLength;

    /// <summary>
    /// Sets the subject which is to be used when replying to the message which has been published by a publisher/requestor.
    /// </summary>
    /// <param name="replyTo">The full reply to address</param>
    public void SetReplyTo(string? replyTo)
    {
        if (replyTo != null)
        {
            ReplyTo = replyTo;
        }
    }
}
