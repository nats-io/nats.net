using NATS.Client.Core;

namespace NATS.Client.JetStream;

public class NatsJSException : NatsException
{
    public NatsJSException(string message)
        : base(message)
    {
    }

    public NatsJSException(string message, Exception exception)
        : base(message, exception)
    {
    }
}
