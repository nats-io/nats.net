using Microsoft.Extensions.Logging;

namespace NATS.Client.KeyValueStore;

public static class NatsKVLogEvents
{
    public static readonly EventId IdleTimeout = new(3001, nameof(IdleTimeout));
    public static readonly EventId NewConsumer = new(3002, nameof(NewConsumer));
    public static readonly EventId NewConsumerCreated = new(3003, nameof(NewConsumerCreated));
    public static readonly EventId DeleteOldDeliverySubject = new(3004, nameof(DeleteOldDeliverySubject));
    public static readonly EventId NewDeliverySubject = new(3005, nameof(NewDeliverySubject));
    public static readonly EventId Protocol = new(3006, nameof(Protocol));
    public static readonly EventId RecreateConsumer = new(3007, nameof(RecreateConsumer));
    public static readonly EventId Internal = new(3008, nameof(Internal));
}
