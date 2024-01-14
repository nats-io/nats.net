using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;

namespace NATS.Client.Hosting.Tests;

public class NatsHostingExtensionsTests
{
    [Fact]
    public void AddNats_RegistersNatsConnectionAsSingleton_WhenPoolSizeIsOne()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        services.AddNats(poolSize: 1);
        var provider = services.BuildServiceProvider();

        var natsConnection1 = provider.GetRequiredService<INatsConnection>();
        var natsConnection2 = provider.GetRequiredService<INatsConnection>();

        Assert.NotNull(natsConnection1);
        Assert.Same(natsConnection1, natsConnection2); // Singleton should return the same instance
    }

    [Fact]
    public void AddNats_RegistersNatsConnectionAsTransient_WhenPoolSizeIsGreaterThanOne()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        services.AddNats(poolSize: 2);
        var provider = services.BuildServiceProvider();

        var natsConnection1 = provider.GetRequiredService<INatsConnection>();
        var natsConnection2 = provider.GetRequiredService<INatsConnection>();

        Assert.NotNull(natsConnection1);
        Assert.NotSame(natsConnection1, natsConnection2); // Transient should return different instances
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void AddNats_RegistersKeyedNatsConnection_WhenKeyIsProvided()
    {
        var key = "TestKey";

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        services.AddNats(poolSize: 1, key: key);
        var provider = services.BuildServiceProvider();

        var natsConnection = provider.GetKeyedService<INatsConnection>(key);
        Assert.NotNull(natsConnection);
    }
#endif
}
