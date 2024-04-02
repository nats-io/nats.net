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
        var key1 = "TestKey1";
        var key2 = "TestKey2";

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        services.AddNats(poolSize: 1, key: key1);
        services.AddNats(poolSize: 1, key: key2);
        var provider = services.BuildServiceProvider();

        var natsConnection1A = provider.GetKeyedService<INatsConnection>(key1);
        Assert.NotNull(natsConnection1A);
        var natsConnection1B = provider.GetKeyedService<INatsConnection>(key1);
        Assert.NotNull(natsConnection1B);
        Assert.Same(natsConnection1A, natsConnection1B);

        var natsConnection2 = provider.GetKeyedService<INatsConnection>(key2);
        Assert.NotNull(natsConnection2);
        Assert.NotSame(natsConnection2, natsConnection1A);
    }

    [Fact]
    public void AddNats_RegistersKeyedNatsConnection_WhenKeyIsProvided_pooled()
    {
        var key1 = "TestKey1";
        var key2 = "TestKey2";

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        services.AddNats(poolSize: 2, key: key1);
        services.AddNats(poolSize: 2, key: key2);
        var provider = services.BuildServiceProvider();

        Dictionary<string, List<object>> connections = new();
        foreach (var key in new[] { key1, key2 })
        {
            var nats1 = provider.GetKeyedService<INatsConnection>(key);
            Assert.NotNull(nats1);
            var nats2 = provider.GetKeyedService<INatsConnection>(key);
            Assert.NotNull(nats2);
            var nats3 = provider.GetKeyedService<INatsConnection>(key);
            Assert.NotNull(nats3);
            var nats4 = provider.GetKeyedService<INatsConnection>(key);
            Assert.NotNull(nats4);

            // relying on the fact that the pool size is 2 and connections are returned in a round-robin fashion
            Assert.NotSame(nats1, nats2);
            Assert.Same(nats1, nats3);
            Assert.NotSame(nats2, nats3);
            Assert.Same(nats2, nats4);

            if (!connections.TryGetValue(key, out var list))
            {
                list = new List<object>();
                connections.Add(key, list);
            }

            list.Add(nats1);
            list.Add(nats2);
            list.Add(nats3);
            list.Add(nats4);
        }

        foreach (var obj1 in connections[key1])
        {
            foreach (var obj2 in connections[key2])
                Assert.NotSame(obj1, obj2);
        }
    }
#endif
}
