namespace NATS.Client.Core;

public interface INatsSubscriptionManagerFactory
{
}

public interface INatsMsgInterceptorFactory
{
}

public interface INatsConnectionFactory
{
}

public record NatsExtensionPoints
{
    public INatsConnectionFactory? ConnectionFactory { get; init; }

    public INatsMsgInterceptorFactory? MsgInterceptorFactory { get; init; }

    public INatsSubscriptionManagerFactory? SubscriptionManagerFactory { get; init; }
}
