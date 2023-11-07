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
