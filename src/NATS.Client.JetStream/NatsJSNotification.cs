namespace NATS.Client.JetStream;

public record NatsJSNotification(int Code, string Description)
{
    public static readonly NatsJSNotification HeartbeatTimeout = new NatsJSNotification(1001, "Heartbeat Timeout");
}
