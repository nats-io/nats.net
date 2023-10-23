using NATS.Client.Core;

namespace NATS.Client.Services;

public class NatsSvcException : NatsException
{
    public NatsSvcException(string message)
        : base(message)
    {
    }

    public NatsSvcException(string message, Exception exception)
        : base(message, exception)
    {
    }
}

public class NatsSvcEndPointException : NatsException
{
    public NatsSvcEndPointException(int code, string message, string? body = default)
        : base(message)
    {
        Code = code;
        Body = body ?? string.Empty;
    }

    public NatsSvcEndPointException(int code, string message, Exception exception)
        : base(message, exception)
    {
        Code = code;
        Body = string.Empty;
    }

    public NatsSvcEndPointException(int code, string message, string body, Exception exception)
        : base(message, exception)
    {
        Code = code;
        Body = body;
    }

    public int Code { get; }

    public string Body { get; }
}
