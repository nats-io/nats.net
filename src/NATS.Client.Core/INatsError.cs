namespace NATS.Client.Core;

public interface INatsError
{
    string Message { get; }
}

public sealed class MessageDroppedError : INatsError
{
    public MessageDroppedError(NatsSubBase subscription, int pending, string subject, string? replyTo, NatsHeaders? headers, object? data)
    {
        Subscription = subscription;
        Pending = pending;
        Subject = subject;
        ReplyTo = replyTo;
        Headers = headers;
        Data = data;
        Message = $"Dropped message from {subject} with {pending} pending messages";
    }

    public NatsSubBase Subscription { get; }

    public int Pending { get; }

    public string Subject { get; }

    public string? ReplyTo { get; }

    public NatsHeaders? Headers { get; }

    public object? Data { get; }

    public string Message { get; }
}
