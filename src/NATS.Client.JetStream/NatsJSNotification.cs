namespace NATS.Client.JetStream;

public class NatsJSNotification
{
    public static readonly NatsJSNotification Timeout = new(code: 100, message: "Timeout");

    public NatsJSNotification(int code, string message)
    {
        Code = code;
        Message = message;
    }

    public int Code { get; }

    public string Message { get; }
}
