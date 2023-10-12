using Microsoft.Extensions.Logging;

namespace NATS.Client.KeyValueStore;

public static class NatsKVLogEvents
{
    public static readonly EventId IdleTimeout = new(3001, nameof(IdleTimeout));
    public static readonly EventId NewConsumer = new(3002, nameof(NewConsumer));
    public static readonly EventId NewConsumerCreated = new(3003, nameof(NewConsumerCreated));
    public static readonly EventId DeleteOldDeliverySubject = new(3004, nameof(DeleteOldDeliverySubject));
    public static readonly EventId NewDeliverySubject = new(3005, nameof(NewDeliverySubject));
}
