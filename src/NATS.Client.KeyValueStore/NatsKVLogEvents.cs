using Microsoft.Extensions.Logging;

namespace NATS.Client.KeyValueStore;

public static class NatsKVLogEvents
{
    public static readonly EventId IdleTimeout = new(3001, nameof(IdleTimeout));
    public static readonly EventId NewConsumer = new(3002, nameof(NewConsumer));
}
