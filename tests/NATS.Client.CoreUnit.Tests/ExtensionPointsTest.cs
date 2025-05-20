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
                ConnectionFactory = new TestHostConnectionFactory(),
                MsgInterceptorFactory = new TestMsgInterceptorFactory(),
                SubscriptionManagerFactory = new TestSubscriptionManagerFactory(),
            },
        };
        await using var connection = new NatsConnection(opts);
    }
}

public class TestSubscriptionManagerFactory : INatsSubscriptionManagerFactory
{
}

public class TestMsgInterceptorFactory : INatsMsgInterceptorFactory
{
}

public class TestHostConnectionFactory : INatsConnectionFactory
{
}
