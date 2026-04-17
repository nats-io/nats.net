namespace NATS.Client.Core;

/// <summary>
/// Provides message envelope metadata available during serialization and deserialization.
/// </summary>
/// <remarks>
/// Use the constructor to create instances. A <c>default(NatsMsgContext)</c> value has a null
/// <see cref="Subject"/> and is not a valid context; the library never passes such a value to
/// user code.
/// </remarks>
public readonly struct NatsMsgContext
{
    /// <summary>Creates a new <see cref="NatsMsgContext"/>.</summary>
    /// <param name="subject">Subject the message was published to.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="headers">Optional message headers.</param>
    public NatsMsgContext(string subject, string? replyTo = null, INatsHeaders? headers = null)
    {
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        ReplyTo = replyTo;
        Headers = headers;
    }

    /// <summary>Subject the message was published to.</summary>
    public string Subject { get; }

    /// <summary>Optional reply-to subject.</summary>
    public string? ReplyTo { get; }

    /// <summary>Optional message headers. May be null if the caller did not supply headers.</summary>
    public INatsHeaders? Headers { get; }
}
