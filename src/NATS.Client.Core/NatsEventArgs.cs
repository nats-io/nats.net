// ReSharper disable UnusedAutoPropertyAccessor.Global - properties are used by consumers outside of this library
namespace NATS.Client.Core;

public class NatsEventArgs : EventArgs
{
    public NatsEventArgs(string message) => Message = message;

    public string Message { get; }
}

public class NatsMessageDroppedEventArgs : NatsEventArgs
{
    public NatsMessageDroppedEventArgs(NatsSubBase subscription, int pending, string subject, string? replyTo, NatsHeaders? headers, object? data)
        : base($"Dropped message from {subject} with {pending} pending messages")
    {
        Subscription = subscription;
        Pending = pending;
        Subject = subject;
        ReplyTo = replyTo;
        Headers = headers;
        Data = data;
    }

    public NatsSubBase Subscription { get; }

    public int Pending { get; }

    public string Subject { get; }

    public string? ReplyTo { get; }

    public NatsHeaders? Headers { get; }

    public object? Data { get; }
}

public class NatsLameDuckModeActivatedEventArgs : NatsEventArgs
{
    public NatsLameDuckModeActivatedEventArgs(Uri uri)
        : base("Lame duck mode activated") => Uri = uri;

    public Uri Uri { get; }
}

public class NatsServerErrorEventArgs : NatsEventArgs
{
    public NatsServerErrorEventArgs(string error)
        : base($"Server error {error}") => Error = error;

    public string Error { get; }
}
