using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// Generic JetStream exception.
/// </summary>
public class NatsJSException : NatsException
{
    /// <summary>
    /// Create JetStream generic exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    public NatsJSException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Create JetStream generic exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="exception">Inner exception.</param>
    public NatsJSException(string message, Exception exception)
        : base(message, exception)
    {
    }
}

/// <summary>
/// JetStream protocol errors received during message consumption.
/// </summary>
public class NatsJSProtocolException : NatsJSException
{
    /// <summary>
    /// Create JetStream protocol exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    public NatsJSProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Create JetStream protocol exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="exception">Inner exception.</param>
    public NatsJSProtocolException(string message, Exception exception)
        : base(message, exception)
    {
    }
}

/// <summary>
/// The exception that is thrown when JetStream publish acknowledgment indicates a duplicate sequence error.
/// </summary>
public class NatsJSDuplicateMessageException : NatsJSException
{
    /// <summary>
    /// Create JetStream duplicate message exception.
    /// </summary>
    /// <param name="sequence">The duplicate sequence number.</param>
    public NatsJSDuplicateMessageException(long sequence)
        : base($"Duplicate of {sequence}") =>
        Sequence = sequence;

    /// <summary>
    /// The duplicate sequence number.
    /// </summary>
    public long Sequence { get; }
}

/// <summary>
/// JetStream API call errors.
/// </summary>
public class NatsJSApiException : NatsJSException
{
    /// <summary>
    /// Create JetStream API exception.
    /// </summary>
    /// <param name="error">Error response received from the server.</param>
    public NatsJSApiException(ApiError error)
        : base(error.Description) =>
        Error = error;

    /// <summary>
    /// API error response received from the server.
    /// </summary>
    public ApiError Error { get; }
}
