namespace NATS.Client.JetStream;

public interface INatsJSNotification
{
    string Name { get; }
}

public class NatsJSTimeoutNotification : INatsJSNotification
{
    public string Name => "Timeout";
}
