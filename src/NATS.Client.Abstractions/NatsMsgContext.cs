namespace NATS.Client.Core;

/// <summary>
/// Provides message envelope metadata available during serialization and deserialization.
/// </summary>
public readonly struct NatsMsgContext
{
    /// <summary>Subject the message was published to.</summary>
    public string Subject { get; init; }

    /// <summary>Optional reply-to subject.</summary>
    public string? ReplyTo { get; init; }

    /// <summary>Optional message headers.</summary>
    public INatsHeaders? Headers { get; init; }
}
