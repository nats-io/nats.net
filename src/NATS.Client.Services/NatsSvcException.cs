using NATS.Client.Core;

namespace NATS.Client.Services;

/// <summary>
/// NATS service exception.
/// </summary>
public class NatsSvcException : NatsException
{
    /// <summary>
    /// Creates a new instance of <see cref="NatsSvcException"/>.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public NatsSvcException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// NATS service endpoint exception.
/// </summary>
public class NatsSvcEndPointException : NatsException
{
    /// <summary>
    /// Creates a new instance of <see cref="NatsSvcEndPointException"/>.
    /// </summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Error message</param>
    /// <param name="body">Optional error body.</param>
    public NatsSvcEndPointException(int code, string message, string? body = default)
        : base(message)
    {
        Code = code;
        Body = body ?? string.Empty;
    }

    /// <summary>
    /// Error code.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// Error body.
    /// </summary>
    public string Body { get; }
}
