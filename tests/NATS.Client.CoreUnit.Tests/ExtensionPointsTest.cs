namespace NATS.Client.CoreUnit.Tests;

public class ExtensionPointsTest
{
    [Fact]
    public async Task T()
    {
        var opts = new NatsOpts
        {
            ExtensionPoints = new NatsExtensionPoints
            {
                SocketConnectionFactory = new TestSocketConnectionFactory(),
                MsgInterceptorFactory = new TestMsgInterceptorFactory(),
                SubscriptionManagerFactory = new TestSubscriptionManagerFactory(),
                RequestReplyProviderFactory = new TestRequestReplyProviderFactory(),
            },
        };
        await using var connection = new NatsConnection(opts);
    }
}

public class TestSocketConnectionFactory : INatsSocketConnectionFactory
{
    public ValueTask<INatsSocketConnection> ConnectAsync(Uri uri, NatsOpts opts, CancellationToken cancellationToken) => throw new NotImplementedException();
}

public class TestSubscriptionManagerFactory : INatsSubscriptionManagerFactory
{
}

public class TestMsgInterceptorFactory : INatsMsgInterceptorFactory
{
}

public class TestRequestReplyProviderFactory : INatsRequestReplyProviderFactory
{
}
