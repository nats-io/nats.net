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
    ActivityContext ParentContext);
