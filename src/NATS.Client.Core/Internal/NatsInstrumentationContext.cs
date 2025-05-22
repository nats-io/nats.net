namespace NATS.Client.Core.Internal;

public readonly record struct NatsInstrumentationContext(
    string Subject,
    NatsHeaders? Headers,
    string? ReplyTo,
    string? QueueGroup,
    long? BodySize,
    long? Size,
    INatsConnection? Connection);
