namespace NATS.Client.Core;

public interface INatsSubscriptionManagerFactory
{
}

public interface INatsMsgInterceptorFactory
{
}

public interface INatsRequestReplyProviderFactory
{
}

public record NatsExtensionPoints
{
    public INatsSocketConnectionFactory? SocketConnectionFactory { get; init; }

    public INatsMsgInterceptorFactory? MsgInterceptorFactory { get; init; }

    public INatsSubscriptionManagerFactory? SubscriptionManagerFactory { get; init; }

    public INatsRequestReplyProviderFactory? RequestReplyProviderFactory { get; init; }
}
