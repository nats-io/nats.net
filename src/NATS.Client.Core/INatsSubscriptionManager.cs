namespace NATS.Client.Core;

public interface INatsSubscriptionManager
{
    public ValueTask RemoveAsync(NatsSubBase sub);
}
