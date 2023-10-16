using NATS.Client.JetStream;

namespace NATS.Client.ObjectStore;

/// <summary>
/// NATS Object Store exception.
/// </summary>
public class NatsOBException : NatsJSException
{
    /// <summary>
    /// Create a new NATS Object Store exception.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public NatsOBException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Create a new NATS Object Store exception.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="exception">Inner exception</param>
    public NatsOBException(string message, Exception exception)
        : base(message, exception)
    {
    }
}
