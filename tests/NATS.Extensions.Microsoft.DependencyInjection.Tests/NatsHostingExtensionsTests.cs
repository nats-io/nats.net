using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.Core.Tests;

namespace NATS.Extensions.Microsoft.DependencyInjection.Tests;

public class NatsHostingExtensionsTests
{
    [Fact]
    public void AddNatsClient_RegistersNatsConnectionAsSingleton_WhenPoolSizeIsOne()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient();

        var provider = services.BuildServiceProvider();
        var natsConnection1 = provider.GetRequiredService<INatsConnection>();
        var natsConnection2 = provider.GetRequiredService<INatsConnection>();

        Assert.NotNull(natsConnection1);
        Assert.Same(natsConnection1, natsConnection2); // Singleton should return the same instance
    }

    [Fact]
    public void AddNatsClient_RegistersNatsConnectionAsTransient_WhenPoolSizeIsGreaterThanOne()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient(nats => nats.WithPoolSize(2));

        var provider = services.BuildServiceProvider();
        var natsConnection1 = provider.GetRequiredService<INatsConnection>();
        var natsConnection2 = provider.GetRequiredService<INatsConnection>();

        Assert.NotNull(natsConnection1);
        Assert.NotSame(natsConnection1, natsConnection2); // Transient should return different instances
    }

    [Fact]
    public async Task AddNatsClient_WithJsonSerializer()
    {
        await using var server = NatsServer.Start();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient(nats =>
        {
            nats.ConfigureOptions(opts => server.ClientOpts(opts));
            nats.AddJsonSerialization(MyJsonContext.Default);
        });

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var sub = await nats.SubscribeCoreAsync<MyData>("foo", cancellationToken: cancellationToken);
        await nats.PingAsync(cancellationToken);
        await nats.PublishAsync("foo", new MyData("bar"), cancellationToken: cancellationToken);

        var msg = await sub.Msgs.ReadAsync(cancellationToken);
        Assert.Equal("bar", msg.Data?.Name);
    }

    [Fact]
    public Task AddNatsClient_ConfigureOptionsSetsUrl()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient(nats => nats.ConfigureOptions(opts => opts with { Url = "url-set" }));

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Equal("url-set", nats.Opts.Url);

        return Task.CompletedTask;
    }

    [Fact]
    public Task AddNatsClient_ConfigureOptionsSetsUrlResolvesServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton<IMyResolvedService>(new MyResolvedService("url-set"));
        services.AddNatsClient(nats => nats.ConfigureOptions((serviceProvider, opts) =>
        {
            opts = opts with
            {
                Url = serviceProvider.GetRequiredService<IMyResolvedService>().GetValue(),
            };

            return opts;
        }));

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Equal("url-set", nats.Opts.Url);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task AddNatsClient_ConfigureConnectionResolvesServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton<IMyResolvedService>(new MyResolvedService("url-set"));

        AsyncEventHandler<NatsEventArgs> handler = async (sender, args) => { };
        services.AddNatsClient(nats => nats.ConfigureConnection((serviceProvider, conn) =>
        {
            conn.OnConnectingAsync = async instance =>
            {
                string resolved = serviceProvider.GetRequiredService<IMyResolvedService>().GetValue();

                return (resolved, instance.Port);
            };
        }));

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<NatsConnection>();

        (string host, int _) = await nats.OnConnectingAsync!((Host: "host", Port: 123));
        Assert.Equal("url-set", host);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void AddNats_RegistersKeyedNatsConnection_WhenKeyIsProvided()
    {
        var key1 = "TestKey1";
        var key2 = "TestKey2";

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        services.AddNatsClient(builder => builder.WithKey(key1));
        services.AddNatsClient(builder => builder.WithKey(key2));

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

        services.AddNatsClient(builder => builder.WithPoolSize(2).WithKey(key1));
        services.AddNatsClient(builder => builder.WithPoolSize(2).WithKey(key2));
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
