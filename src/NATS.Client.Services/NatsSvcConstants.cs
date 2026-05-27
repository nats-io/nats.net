namespace NATS.Client.Services;

/// <summary>
/// Protocol constants for NATS Services.
/// </summary>
public static class NatsSvcConstants
{
    /// <summary>
    /// Response header carrying the service error message.
    /// </summary>
    public const string ServiceErrorHeader = "Nats-Service-Error";

    /// <summary>
    /// Response header carrying the service error code.
    /// </summary>
    public const string ServiceErrorCodeHeader = "Nats-Service-Error-Code";
}
