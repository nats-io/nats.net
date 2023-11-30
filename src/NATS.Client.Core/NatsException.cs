namespace NATS.Client.Core;

public class NatsException : Exception
{
    public NatsException(string message)
        : base(message)
    {
    }

    public NatsException(string message, Exception exception)
        : base(message, exception)
    {
    }
}

public sealed class NatsNoReplyException : NatsException
{
    public NatsNoReplyException()
        : base("No reply received")
    {
    }
}

public sealed class NatsNoRespondersException : NatsException
{
    public NatsNoRespondersException()
        : base("No responders")
    {
    }
}

public sealed class NatsServerException : NatsException
{
    public NatsServerException(string error)
        : base($"Server error: {error}")
    {
        Error = error;
        IsAuthError = Error.Contains("authorization violation", StringComparison.OrdinalIgnoreCase)
                      || Error.Contains("user authentication expired", StringComparison.OrdinalIgnoreCase)
                      || Error.Contains("user authentication revoked", StringComparison.OrdinalIgnoreCase)
                      || Error.Contains("account authentication expired", StringComparison.OrdinalIgnoreCase);
    }

    public string Error { get; }

    public bool IsAuthError { get; }
}
