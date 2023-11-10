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
    /// <param name="headerCode">Server error code</param>
    /// <param name="headerMessage">Server error message enum (if defined)</param>
    /// <param name="headerMessageText">Server error message string</param>
    public NatsJSProtocolException(int headerCode, NatsHeaders.Messages headerMessage, string headerMessageText)
        : base($"JetStream server error: {headerCode} {headerMessageText}")
    {
        HeaderCode = headerCode;
        HeaderMessage = headerMessage;
        HeaderMessageText = headerMessageText;
    }

    public int HeaderCode { get; }

    public NatsHeaders.Messages HeaderMessage { get; }

    public string HeaderMessageText { get; }
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
    public NatsJSDuplicateMessageException(ulong sequence)
        : base($"Duplicate of {sequence}") =>
        Sequence = sequence;

    /// <summary>
    /// The duplicate sequence number.
    /// </summary>
    public ulong Sequence { get; }
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

public class NatsJSPublishNoResponseException : NatsJSException
{
    public NatsJSPublishNoResponseException()
        : base("No response received from the server")
    {
    }
}

public class NatsJSApiNoResponseException : NatsJSException
{
    public NatsJSApiNoResponseException()
        : base("No API response received from the server")
    {
    }
}

public class NatsJSTimeoutException : NatsJSException
{
    public NatsJSTimeoutException(string type)
        : base($"{type} timed out") =>
        Type = type;

    public string Type { get; }
}

public class NatsJSConnectionException : NatsJSException
{
    public NatsJSConnectionException(string reason)
        : base($"Connection error: {reason}") =>
        Reason = reason;

    public string Reason { get; }
}
