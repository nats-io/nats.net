using System.Diagnostics;

namespace NATS.Client.Core;

public readonly record struct NatsInstrumentationContext(
    string Subject,
    NatsHeaders? Headers,
    string? ReplyTo,
    string? QueueGroup,
    long? BodySize,
    long? Size,
    INatsConnection? Connection,
    ActivityContext ParentContext)
{
    /// <summary>
    /// Gets the baggage extracted from the message headers for receive operations when
    /// <see cref="NatsInstrumentationOptions.PropagateBaggage"/> is enabled (after
    /// <see cref="NatsInstrumentationOptions.BaggageKeyFilter"/> is applied); otherwise null.
    /// Always null for send operations. Enumeration order is unspecified.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string?>>? Baggage { get; init; }
}
