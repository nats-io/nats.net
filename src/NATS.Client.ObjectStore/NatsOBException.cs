using NATS.Client.JetStream;

namespace NATS.Client.ObjectStore;

public class NatsOBException : NatsJSException
{
    public NatsOBException(string message)
        : base(message)
    {
    }

    public NatsOBException(string message, Exception exception)
        : base(message, exception)
    {
    }
}
