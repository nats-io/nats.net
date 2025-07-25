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

public record NatsJSProtocolNotification(string Name, int HeaderCode, string HeaderMessageText) : INatsJSNotification;
