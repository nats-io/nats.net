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
