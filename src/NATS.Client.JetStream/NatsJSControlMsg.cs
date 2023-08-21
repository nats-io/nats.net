using NATS.Client.Core;

namespace NATS.Client.JetStream;

public enum NatsJSControlErrorType
{
    Internal,
    Server,
}

public class NatsJSControlError
{
    public NatsJSControlError(string error) => Error = error;

    public NatsJSControlErrorType Type { get; init; } = NatsJSControlErrorType.Internal;

    public string Error { get; }

    public Exception? Exception { get; init; }

    public string Details { get; init; } = string.Empty;
}

public class NatsJSControlMsg
{
    public static readonly NatsJSControlMsg HeartBeat = new("Heartbeat");

    public NatsJSControlMsg(string name) => Name = name;

    public string Name { get; }

    public NatsHeaders? Headers { get; init; }

    public NatsJSControlError? Error { get; init; }
}
