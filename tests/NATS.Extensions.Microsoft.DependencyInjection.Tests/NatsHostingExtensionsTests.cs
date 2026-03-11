using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Net;
using Synadia.Orbit.Testing.NatsServerProcessManager;

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

        var natsClient1 = provider.GetRequiredService<INatsClient>();
        var natsClient2 = provider.GetRequiredService<INatsClient>();

        Assert.NotNull(natsClient1);
        Assert.Same(natsClient1, natsClient2);
        Assert.Same(natsClient1, natsConnection1); // Same Connection implements INatsClient
        Assert.Same(natsClient1.Connection, natsConnection1);
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
        var natsConnection3 = provider.GetRequiredService<INatsConnection>();
        var natsConnection4 = provider.GetRequiredService<INatsConnection>();

        Assert.NotNull(natsConnection1);
        Assert.NotSame(natsConnection1, natsConnection2); // Transient should return different instances
        Assert.NotSame(natsConnection3, natsConnection4);
        Assert.Same(natsConnection1, natsConnection3); // The pool is round-robin
        Assert.Same(natsConnection2, natsConnection4);

        var natsClient1 = provider.GetRequiredService<INatsClient>();
        var natsClient2 = provider.GetRequiredService<INatsClient>();
        var natsClient3 = provider.GetRequiredService<INatsClient>();
        var natsClient4 = provider.GetRequiredService<INatsClient>();

        Assert.NotNull(natsClient1);
        Assert.NotSame(natsClient1, natsClient2);
        Assert.NotSame(natsClient3, natsClient4);
        Assert.Same(natsClient1, natsClient3);
        Assert.Same(natsClient2, natsClient4);
        Assert.Same(natsClient1, natsConnection1);
        Assert.Same(natsClient1.Connection, natsConnection1);
    }

    [Fact]
    public Task AddNatsClient_OptionsWithDefaults()
    {
        var services = new ServiceCollection();
        services.AddNatsClient();

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Same(NullLoggerFactory.Instance, nats.Opts.LoggerFactory);

        // These defaults are different from NatsOptions defaults but same as NatsClient defaults
        // for ease of use for new users
        Assert.Same(NatsClientDefaultSerializerRegistry.Default, nats.Opts.SerializerRegistry);
        Assert.Equal(BoundedChannelFullMode.Wait, nats.Opts.SubPendingChannelFullMode);

        return Task.CompletedTask;
    }

    [Fact]
    public Task AddNatsClient_WithDefaultSerializerExplicitlySet()
    {
        var services = new ServiceCollection();
        services.AddNatsClient(nats =>
        {
            // These two settings make the options same as NatsOptions defaults
            nats.WithSerializerRegistry(NatsDefaultSerializerRegistry.Default)
                .WithSubPendingChannelFullMode(BoundedChannelFullMode.DropNewest);
        });

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Same(NatsDefaultSerializerRegistry.Default, nats.Opts.SerializerRegistry);
        Assert.Equal(BoundedChannelFullMode.DropNewest, nats.Opts.SubPendingChannelFullMode);

        return Task.CompletedTask;
    }

    [Fact]
    public Task AddNatsClient_WithSerializerExplicitlySet()
    {
        var mySerializerRegistry = new NatsJsonContextSerializerRegistry(MyJsonContext.Default);

        var services = new ServiceCollection();
        services.AddNatsClient(nats =>
        {
#pragma warning disable CS0618 // Type or member is obsolete
            nats.ConfigureOptions(opts => opts with { SerializerRegistry = mySerializerRegistry });
#pragma warning restore CS0618 // Type or member is obsolete
        });

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Same(mySerializerRegistry, nats.Opts.SerializerRegistry);

        // You can only override this using .WithSubPendingChannelFullMode() on builder above
        Assert.Equal(BoundedChannelFullMode.Wait, nats.Opts.SubPendingChannelFullMode);

        return Task.CompletedTask;
    }

    [Fact]
    public Task AddNatsClient_WithSerializerExplicitlySet_optionsBuilder()
    {
        var mySerializerRegistry = new NatsJsonContextSerializerRegistry(MyJsonContext.Default);

        var services = new ServiceCollection();
        services.AddNatsClient(nats =>
        {
            nats.ConfigureOptions(builder => builder.Configure(opts =>
                opts.Opts = opts.Opts with { SerializerRegistry = mySerializerRegistry }));
        });

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Same(mySerializerRegistry, nats.Opts.SerializerRegistry);

        // You can only override this using .WithSubPendingChannelFullMode() on builder above
        Assert.Equal(BoundedChannelFullMode.Wait, nats.Opts.SubPendingChannelFullMode);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task AddNatsClient_WithDefaultSerializer()
    {
        await using var server = await NatsServerProcess.StartAsync();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        // Default JSON serialization
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddNatsClient(nats =>
            {
#pragma warning disable CS0618 // Type or member is obsolete
                nats.ConfigureOptions(opts => opts with { Url = server.Url });
#pragma warning restore CS0618 // Type or member is obsolete
            });

            var provider = services.BuildServiceProvider();
            var nats = provider.GetRequiredService<INatsConnection>();

            // Ad-hoc JSON serialization
            await using var sub = await nats.SubscribeCoreAsync<MyAdHocData>("foo", cancellationToken: cancellationToken);
            await nats.PingAsync(cancellationToken);
            await nats.PublishAsync("foo", new MyAdHocData(1, "bar"), cancellationToken: cancellationToken);

            var msg = await sub.Msgs.ReadAsync(cancellationToken);
            Assert.Equal(1, msg.Data?.Id);
            Assert.Equal("bar", msg.Data?.Name);
        }

        // Default raw serialization
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddNatsClient(nats =>
            {
#pragma warning disable CS0618 // Type or member is obsolete
                nats.ConfigureOptions(opts => opts with { Url = server.Url });
#pragma warning restore CS0618 // Type or member is obsolete
                nats.WithSerializerRegistry(NatsDefaultSerializerRegistry.Default);
            });

            var provider = services.BuildServiceProvider();
            var nats = provider.GetRequiredService<INatsConnection>();

            var exception = await Assert.ThrowsAsync<NatsException>(async () =>
            {
                await nats.PublishAsync("foo", new MyAdHocData(1, "bar"), cancellationToken: cancellationToken);
            });
            Assert.Matches("Can't serialize.*MyAdHocData", exception.Message);
        }
    }

    [Fact]
    public async Task AddNatsClient_WithDefaultSerializer_optionsBuilder()
    {
        await using var server = await NatsServerProcess.StartAsync();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        // Default JSON serialization
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddNatsClient(nats =>
            {
                nats.ConfigureOptions(builder => builder.Configure(opts =>
                    opts.Opts = opts.Opts with { Url = server.Url }));
            });

            var provider = services.BuildServiceProvider();
            var nats = provider.GetRequiredService<INatsConnection>();

            // Ad-hoc JSON serialization
            await using var sub = await nats.SubscribeCoreAsync<MyAdHocData>("foo", cancellationToken: cancellationToken);
            await nats.PingAsync(cancellationToken);
            await nats.PublishAsync("foo", new MyAdHocData(1, "bar"), cancellationToken: cancellationToken);

            var msg = await sub.Msgs.ReadAsync(cancellationToken);
            Assert.Equal(1, msg.Data?.Id);
            Assert.Equal("bar", msg.Data?.Name);
        }

        // Default raw serialization
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddNatsClient(nats =>
            {
                nats.ConfigureOptions(builder => builder.Configure(opts =>
                    opts.Opts = opts.Opts with { Url = server.Url }));
                nats.WithSerializerRegistry(NatsDefaultSerializerRegistry.Default);
            });

            var provider = services.BuildServiceProvider();
            var nats = provider.GetRequiredService<INatsConnection>();

            var exception = await Assert.ThrowsAsync<NatsException>(async () =>
            {
                await nats.PublishAsync("foo", new MyAdHocData(1, "bar"), cancellationToken: cancellationToken);
            });
            Assert.Matches("Can't serialize.*MyAdHocData", exception.Message);
        }
    }

    [Fact]
    public void AddNatsClient_RegistersNatsConnectionAsTransient_WhenPoolSizeFuncIsGreaterThanOne()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient(nats => nats.WithPoolSize(_ => 2));

        var provider = services.BuildServiceProvider();
        var natsConnection1 = provider.GetRequiredService<INatsConnection>();
        var natsConnection2 = provider.GetRequiredService<INatsConnection>();

        Assert.NotNull(natsConnection1);
        Assert.NotSame(natsConnection1, natsConnection2); // Transient should return different instances
    }

    [Fact]
    public async Task AddNatsClient_WithJsonSerializer()
    {
        await using var server = await NatsServerProcess.StartAsync();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient(nats =>
        {
#pragma warning disable CS0618 // Type or member is obsolete
            nats.ConfigureOptions(opts => opts with { Url = server.Url });
            nats.AddJsonSerialization(MyJsonContext.Default);
#pragma warning restore CS0618 // Type or member is obsolete
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
    public async Task AddNatsClient_WithJsonSerializer_optionsBuilder()
    {
        await using var server = await NatsServerProcess.StartAsync();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient(nats =>
        {
            nats.ConfigureOptions(builder => builder.Configure(opts =>
                opts.Opts = opts.Opts with { Url = server.Url }));
#pragma warning disable CS0618 // Type or member is obsolete
            nats.AddJsonSerialization(MyJsonContext.Default);
#pragma warning restore CS0618 // Type or member is obsolete
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
        services.AddNatsClient(nats =>
        {
#pragma warning disable CS0618 // Type or member is obsolete
            nats.ConfigureOptions(opts => opts with { Url = "url-set" });
#pragma warning restore CS0618 // Type or member is obsolete
        });

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Equal("url-set", nats.Opts.Url);

        return Task.CompletedTask;
    }

    [Fact]
    public Task AddNatsClient_ConfigureOptionsSetsUrl_optionsBuilder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient(nats => nats.ConfigureOptions(builder => builder.Configure(opts =>
            opts.Opts = opts.Opts with { Url = "url-set" })));

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Equal("url-set", nats.Opts.Url);

        return Task.CompletedTask;
    }

    [Fact]
    public Task AddNatsClient_ConfigureOptionsSetsUrl_backwardsCompatibility()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddNatsClient(nats =>
        {
#pragma warning disable CS0618 // Type or member is obsolete
            nats.ConfigureOptions(opts => opts with { Url = "url-set" });
#pragma warning restore CS0618 // Type or member is obsolete
            nats.ConfigureOptions(builder => builder.Configure(opts =>
                opts.Opts = opts.Opts with { Url = $"new-{opts.Opts.Url}" }));
        });

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Equal("new-url-set", nats.Opts.Url);

        return Task.CompletedTask;
    }

    [Fact]
    public Task AddNatsClient_ConfigureOptionsSetsUrlResolvesServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton<IMyResolvedService>(new MyResolvedService("url-set"));
        services.AddNatsClient(nats =>
        {
#pragma warning disable CS0618 // Type or member is obsolete
            nats.ConfigureOptions((_, opts) => opts); // Add multiple to test chaining
            nats.ConfigureOptions((serviceProvider, opts) =>
            {
                opts = opts with
                {
                    Url = serviceProvider.GetRequiredService<IMyResolvedService>().GetValue(),
                };

                return opts;
            });
#pragma warning restore CS0618 // Type or member is obsolete
        });

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<INatsConnection>();

        Assert.Equal("url-set", nats.Opts.Url);

        return Task.CompletedTask;
    }

    [Fact]
    public Task AddNatsClient_ConfigureOptionsSetsUrlResolvesServices_optionsBuilder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton<IMyResolvedService>(new MyResolvedService("url-set"));
        services.AddNatsClient(nats => nats
            .ConfigureOptions(builder => { }) // Add multiple to test chaining
            .ConfigureOptions(builder => builder.Configure<IServiceProvider>((opts, serviceProvider) =>
            {
                opts.Opts = opts.Opts with
                {
                    Url = serviceProvider.GetRequiredService<IMyResolvedService>().GetValue(),
                };
            })));

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
        services.AddNatsClient(nats => nats
            .ConfigureConnection((_, _) => { }) // Add multiple to test chaining
            .ConfigureConnection((serviceProvider, conn) =>
            {
                conn.OnConnectingAsync = instance =>
                {
                    var resolved = serviceProvider.GetRequiredService<IMyResolvedService>().GetValue();

                    return new ValueTask<(string Host, int Port)>((resolved, instance.Port));
                };
            }));

        var provider = services.BuildServiceProvider();
        var nats = provider.GetRequiredService<NatsConnection>();

        (var host, var _) = await nats.OnConnectingAsync!((Host: "host", Port: 123));
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

    [Fact]
    public void AddNats_RegistersKeyedNatsConnection_WhenKeyIsProvided_pooledFunc()
    {
        var key1 = "TestKey1";
        var key2 = "TestKey2";

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        services.AddNatsClient(builder => builder.WithPoolSize(_ => 2).WithKey(key1));
        services.AddNatsClient(builder => builder.WithPoolSize(_ => 2).WithKey(key2));
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

    /// <summary>
    /// Test for GitHub issue #1003: NatsOptsBuilder options are overwritten when multiple keyed clients are registered.
    /// When injecting NATS twice with different keys, each injection should use its own configuration,
    /// not the configuration from another injection.
    /// </summary>
    /// <see href="https://github.com/nats-io/nats.net/issues/1003"/>
    [Fact]
    public void AddNats_KeyedConnections_ShouldUseTheirOwnConfiguration()
    {
        var key1 = "FirstNATS";
        var key2 = "SecondNATS";
        var url1 = "nats://first:4222";
        var url2 = "nats://second:4222";

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        // Register first NATS client with key1 and url1
        services.AddNatsClient(builder => builder
            .WithKey(key1)
            .ConfigureOptions(optionsBuilder =>
                optionsBuilder.Configure(opts =>
                    opts.Opts = opts.Opts with { Url = url1 })));

        // Register second NATS client with key2 and url2
        services.AddNatsClient(builder => builder
            .WithKey(key2)
            .ConfigureOptions(optionsBuilder =>
                optionsBuilder.Configure(opts =>
                    opts.Opts = opts.Opts with { Url = url2 })));

        var provider = services.BuildServiceProvider();

        // Resolve both keyed connections
        var nats1 = provider.GetRequiredKeyedService<INatsConnection>(key1);
        var nats2 = provider.GetRequiredKeyedService<INatsConnection>(key2);

        // Each connection should have its own configured URL
        Assert.Equal(url1, nats1.Opts.Url);
        Assert.Equal(url2, nats2.Opts.Url);
    }

    [Fact]
    public void AddNats_WithKey_ThrowsIfCalledAfterConfigureOptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddNatsClient(builder => builder
                .ConfigureOptions(opts => { }) // Called first
                .WithKey("TestKey"));          // Should throw
        });

        Assert.Contains("WithKey must be called before", exception.Message);
    }
#endif
}

public record MyAdHocData(int Id, string Name);
