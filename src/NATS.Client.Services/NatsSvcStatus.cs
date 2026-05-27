namespace NATS.Client.Services;

/// <summary>
/// Status of a NATS service response, derived from the <c>Nats-Service-Error</c>
/// and <c>Nats-Service-Error-Code</c> response headers and the no-responders sentinel.
/// </summary>
public readonly struct NatsSvcStatus
{
    private NatsSvcStatus(int code, string? message, bool hasNoResponders)
    {
        Code = code;
        Message = message;
        HasNoResponders = hasNoResponders;
    }

    /// <summary>
    /// Error code from the <c>Nats-Service-Error-Code</c> header. <c>0</c> when the header
    /// is missing or not an integer, or when the response is a success.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// Error message from the <c>Nats-Service-Error</c> header, or <c>null</c> when absent.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// <c>true</c> when the response is a no-responders sentinel (no service was listening).
    /// </summary>
    public bool HasNoResponders { get; }

    /// <summary>
    /// <c>true</c> when the response carries no service error and is not a no-responders sentinel.
    /// </summary>
    public bool IsSuccess => Message is null && !HasNoResponders;

    internal static NatsSvcStatus Success { get; } = new(0, null, false);

    internal static NatsSvcStatus NoResponders { get; } = new(0, null, true);

    internal static NatsSvcStatus FromError(int code, string message) => new(code, message, false);
}
