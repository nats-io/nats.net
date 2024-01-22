using Microsoft.Extensions.Logging;

namespace NATS.Client.Core;

public static class NatsLogEvents
{
    public static readonly EventId Connection = new(1001, nameof(Connection));
    public static readonly EventId Subscription = new(1002, nameof(Subscription));
    public static readonly EventId Security = new(1003, nameof(Security));
    public static readonly EventId InboxSubscription = new(1004, nameof(InboxSubscription));
    public static readonly EventId Protocol = new(1005, nameof(Protocol));
    public static readonly EventId TcpSocket = new(1006, nameof(TcpSocket));
    public static readonly EventId Internal = new(1006, nameof(Internal));
    public static readonly EventId Buffer = new(1007, nameof(Buffer));
}
