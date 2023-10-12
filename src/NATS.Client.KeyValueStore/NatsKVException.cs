using NATS.Client.JetStream;

namespace NATS.Client.KeyValueStore;

/// <summary>
/// Key Value Store exception
/// </summary>
public class NatsKVException : NatsJSException
{
    /// <summary>
    /// Create a new Key Value Store exception
    /// </summary>
    /// <param name="message">Exception message</param>
    public NatsKVException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Create a new Key Value Store exception
    /// </summary>
    /// <param name="message">Exception message</param>
    /// <param name="exception">Inner exception</param>
    public NatsKVException(string message, Exception exception)
        : base(message, exception)
    {
    }
}
