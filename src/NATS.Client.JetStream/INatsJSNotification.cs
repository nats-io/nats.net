using NATS.Client.Core;

namespace NATS.Client.JetStream;

public interface INatsJSNotification
{
    string Name { get; }
}

public record NatsJSTimeoutNotification : INatsJSNotification
{
    public string Name => "Timeout";
}

public record NatsJSProtocolNotification(string Name, int HeaderCode, string HeaderMessageText) : INatsJSNotification;
