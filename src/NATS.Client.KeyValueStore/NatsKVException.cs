using NATS.Client.JetStream;

namespace NATS.Client.KeyValueStore;

public class NatsKVException : NatsJSException
{
    public NatsKVException(string message)
        : base(message)
    {
    }

    public NatsKVException(string message, Exception exception)
        : base(message, exception)
    {
    }
}
