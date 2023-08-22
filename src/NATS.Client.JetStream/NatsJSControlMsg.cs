using NATS.Client.Core;

namespace NATS.Client.JetStream;

public enum NatsJSControlType
{
    Error,
    Headers,
}

public class NatsJSControlError
{
    public NatsJSControlError(string error) => Error = error;

    public string Error { get; }

    public Exception? Exception { get; init; }

    public string Details { get; init; } = string.Empty;
}

public class NatsJSControlMsg
{
    public NatsJSControlMsg(NatsJSControlType type) => Type = type;

    public NatsJSControlType Type { get; }


    public NatsHeaders? Headers { get; init; }

    public NatsJSControlError? Error { get; init; }
}
