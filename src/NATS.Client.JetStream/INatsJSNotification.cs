namespace NATS.Client.JetStream;

public interface INatsJSNotification
{
    string Name { get; }
}

public record NatsJSTimeoutNotification : INatsJSNotification
{
    public static readonly NatsJSTimeoutNotification Default = new();

    public string Name => "Timeout";
}

public record NatsJSNoRespondersNotification : INatsJSNotification
{
    public static readonly NatsJSNoRespondersNotification Default = new();

    public string Name => "No Responders";
}

public record NatsJSLeadershipChangeNotification : INatsJSNotification
{
    public static readonly NatsJSLeadershipChangeNotification Default = new();

    public string Name => "Leadership Change";
}

public record NatsJSMessageSizeExceedsMaxBytesNotification : INatsJSNotification
{
    public static readonly NatsJSMessageSizeExceedsMaxBytesNotification Default = new();

    public string Name => "Message Size Exceeds MaxBytes";
}

/// <summary>
/// Notification sent when the server reports a pin ID mismatch for pinned client priority policy.
/// </summary>
/// <remarks>
/// This notification indicates that the client's pin ID doesn't match what the server expects.
/// The client should clear its pin ID and retry the request to get a new pin.
/// </remarks>
public record NatsJSPinIdMismatchNotification : INatsJSNotification
{
    public static readonly NatsJSPinIdMismatchNotification Default = new();

    public string Name => "Pin ID Mismatch";
}

public record NatsJSProtocolNotification(string Name, int HeaderCode, string HeaderMessageText) : INatsJSNotification;
