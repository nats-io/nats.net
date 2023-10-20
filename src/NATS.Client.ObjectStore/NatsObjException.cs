using NATS.Client.JetStream;

namespace NATS.Client.ObjectStore;

/// <summary>
/// NATS Object Store exception.
/// </summary>
public class NatsObjException : NatsJSException
{
    /// <summary>
    /// Create a new NATS Object Store exception.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public NatsObjException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Create a new NATS Object Store exception.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="exception">Inner exception</param>
    public NatsObjException(string message, Exception exception)
        : base(message, exception)
    {
    }
}

/// <summary>
/// NATS Object Store object not found exception.
/// </summary>
public class NatsObjNotFoundException : NatsObjException
{
    /// <summary>
    /// Create a new NATS Object Store object not found exception.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public NatsObjNotFoundException(string message)
        : base(message)
    {
    }
}
