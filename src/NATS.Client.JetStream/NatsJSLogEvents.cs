using Microsoft.Extensions.Logging;

namespace NATS.Client.JetStream;

public static class NatsJSLogEvents
{
    public static readonly EventId Config = new(2001, nameof(Config));
    public static readonly EventId Timeout = new(2002, nameof(Timeout));
    public static readonly EventId IdleTimeout = new(2003, nameof(IdleTimeout));
    public static readonly EventId Expired = new(2004, nameof(Expired));
    public static readonly EventId ProtocolMessage = new(2005, nameof(ProtocolMessage));
    public static readonly EventId Headers = new(2006, nameof(Headers));
    public static readonly EventId PendingCount = new(2007, nameof(PendingCount));
    public static readonly EventId MessageProperty = new(2008, nameof(MessageProperty));
    public static readonly EventId PullRequest = new(2009, nameof(PullRequest));
}
