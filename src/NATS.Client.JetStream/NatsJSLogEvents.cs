using Microsoft.Extensions.Logging;

namespace NATS.Client.JetStream;

public static class NatsJSLogEvents
{
    public static readonly EventId Config = new(1001, nameof(Config));
    public static readonly EventId Timeout = new(1002, nameof(Timeout));
    public static readonly EventId IdleTimeout = new(1003, nameof(IdleTimeout));
    public static readonly EventId Expired = new(1004, nameof(Expired));
    public static readonly EventId ProtocolMessage = new(1005, nameof(ProtocolMessage));
    public static readonly EventId Headers = new(1006, nameof(Headers));
    public static readonly EventId PendingCount = new(1007, nameof(PendingCount));
    public static readonly EventId MessageProperty = new(1008, nameof(MessageProperty));
    public static readonly EventId PullRequest = new(1009, nameof(PullRequest));
}
