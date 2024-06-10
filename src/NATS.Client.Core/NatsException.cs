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
        IsAuthError = Error.Contains("authorization violation")
                      || Error.Contains("user authentication expired")
                      || Error.Contains("user authentication revoked")
                      || Error.Contains("account authentication expired");
    }

    public string Error { get; }

    public bool IsAuthError { get; }
}
