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

public class NatsKVKeyDeletedException : NatsKVException
{
    public NatsKVKeyDeletedException(ulong revision)
        : base("Key was deleted") =>
        Revision = revision;

    public ulong Revision { get; }
}

public class NatsKVWrongLastRevisionException : NatsKVException
{
    public NatsKVWrongLastRevisionException()
        : base("Wrong last revision")
    {
    }
}

public class NatsKVCreateException : NatsKVException
{
    public NatsKVCreateException()
        : base("Can't create entry")
    {
    }
}

public class NatsKVKeyNotFoundException : NatsKVException
{
    public static readonly NatsKVKeyNotFoundException Default = new();

    public NatsKVKeyNotFoundException()
        : base("Key not found")
    {
    }
}
