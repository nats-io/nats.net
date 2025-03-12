using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Net;

namespace NATS.Extensions.Microsoft.DependencyInjection;

public class NatsBuilder
{
    private readonly IServiceCollection _services;

    private Func<IServiceProvider, int> _poolSizeConfigurer = _ => 1;
    private Func<IServiceProvider, NatsOpts, NatsOpts>? _configureOpts;
    private Action<IServiceProvider, NatsConnection>? _configureConnection;
    private object? _diKey = null;
    private BoundedChannelFullMode? _pending = null;
    private INatsSerializerRegistry? _serializerRegistry = null;

    public NatsBuilder(IServiceCollection services)
        => _services = services;

    public NatsBuilder WithPoolSize(int size)
    {
        _poolSizeConfigurer = _ => Math.Max(size, 1);

        return this;
    }

    public NatsBuilder WithPoolSize(Func<IServiceProvider, int> sizeConfigurer)
    {
        _poolSizeConfigurer = sp => Math.Max(sizeConfigurer(sp), 1);

        return this;
    }

    public NatsBuilder ConfigureOptions(Func<NatsOpts, NatsOpts> optsFactory) =>
        ConfigureOptions((_, opts) => optsFactory(opts));

    public NatsBuilder ConfigureOptions(Func<IServiceProvider, NatsOpts, NatsOpts> optsFactory)
    {
        var configure = _configureOpts;
        _configureOpts = (serviceProvider, opts) =>
        {
            opts = configure?.Invoke(serviceProvider, opts) ?? opts;

            return optsFactory(serviceProvider, opts);
        };

        return this;
    }

    public NatsBuilder ConfigureConnection(Action<NatsConnection> configureConnection) =>
        ConfigureConnection((_, con) => configureConnection(con));

    public NatsBuilder ConfigureConnection(Action<IServiceProvider, NatsConnection> configureConnection)
    {
        var configure = _configureConnection;
        _configureConnection = (serviceProvider, connection) =>
        {
            configure?.Invoke(serviceProvider, connection);

            configureConnection(serviceProvider, connection);
        };

        return this;
    }

    [Obsolete("By default JSON serialization is enabled. If you still want to use generated JSON serialization, use WithSerializerRegistry, JsonSerializerContext and optionally NatsSerializerBuilder")]
    public NatsBuilder AddJsonSerialization(JsonSerializerContext context) =>
        AddJsonSerialization(_ => context);

    [Obsolete("By default JSON serialization is enabled. If you still want to use generated JSON serialization, use WithSerializerRegistry, JsonSerializerContext and optionally NatsSerializerBuilder")]
    public NatsBuilder AddJsonSerialization(Func<IServiceProvider, JsonSerializerContext> contextFactory)
        => ConfigureOptions((serviceProvider, opts) =>
        {
            var context = contextFactory(serviceProvider);
            NatsJsonContextSerializerRegistry jsonRegistry = new(context);

            return opts with { SerializerRegistry = jsonRegistry };
        });

#if NET8_0_OR_GREATER
    public NatsBuilder WithKey(object key)
    {
        _diKey = key;

        return this;
    }
#endif

    /// <summary>
    /// Override the default <see cref="BoundedChannelFullMode"/> for the pending messages channel.
    /// </summary>
    /// <param name="pending">Full mode for the subscription channel.</param>
    /// <returns>Builder to allow method chaining.</returns>
    /// <remarks>
    /// This will be applied to options overriding values set for <c>SubPendingChannelFullMode</c> in options.
    /// By default, the pending messages channel will wait for space to be available when full.
    /// Note that this is not the same as <c>NatsOpts</c> default <c>SubPendingChannelFullMode</c> which is <c>DropNewest</c>.
    /// </remarks>
    public NatsBuilder WithSubPendingChannelFullMode(BoundedChannelFullMode pending)
    {
        _pending = pending;
        return this;
    }

    /// <summary>
    /// Override the default <see cref="INatsSerializerRegistry"/> for the options.
    /// </summary>
    /// <param name="registry">Serializer registry to use.</param>
    /// <returns>Builder to allow method chaining.</returns>
    /// <remarks>
    /// This will be applied to options overriding values set for <c>SerializerRegistry</c> in options.
    /// By default, NatsClient registry will be used which allows ad-hoc JSON serialization.
    /// Note that this is not the same as <c>NatsOpts</c> default <c>SerializerRegistry</c> which
    /// doesn't do ad-hoc JSON serialization.
    /// </remarks>
    public NatsBuilder WithSerializerRegistry(INatsSerializerRegistry registry)
    {
        _serializerRegistry = registry;
        return this;
    }

    internal IServiceCollection Build()
    {
        if (_diKey == null)
        {
            _services.TryAddSingleton<NatsConnectionPool>(provider => PoolFactory(provider));
            _services.TryAddSingleton<INatsConnectionPool>(static provider => provider.GetRequiredService<NatsConnectionPool>());
            _services.TryAddTransient<NatsConnection>(static provider => PooledConnectionFactory(provider, null));
            _services.TryAddTransient<INatsConnection>(static provider => provider.GetRequiredService<NatsConnection>());
            _services.TryAddTransient<INatsClient>(static provider => provider.GetRequiredService<NatsConnection>());
        }
        else
        {
#if NET8_0_OR_GREATER
            _services.TryAddKeyedSingleton<NatsConnectionPool>(_diKey, PoolFactory);
            _services.TryAddKeyedSingleton<INatsConnectionPool>(_diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnectionPool>(key));
            _services.TryAddKeyedTransient<NatsConnection>(_diKey, PooledConnectionFactory);
            _services.TryAddKeyedTransient<INatsConnection>(_diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnection>(key));
            _services.TryAddKeyedTransient<INatsClient>(_diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnection>(key));
#endif
        }

        return _services;
    }

    private static NatsConnection PooledConnectionFactory(IServiceProvider provider, object? key)
    {
#if NET8_0_OR_GREATER
        if (key != null)
        {
            var keyedConnection = provider.GetRequiredKeyedService<NatsConnectionPool>(key).GetConnection();
            return keyedConnection as NatsConnection ?? throw new InvalidOperationException("Connection is not of type NatsConnection");
        }
#endif
        var connection = provider.GetRequiredService<NatsConnectionPool>().GetConnection();
        return connection as NatsConnection ?? throw new InvalidOperationException("Connection is not of type NatsConnection");
    }

    private NatsConnectionPool PoolFactory(IServiceProvider provider, object? diKey = null)
    {
        var options = GetNatsOpts(provider);

        return new NatsConnectionPool(_poolSizeConfigurer(provider), options, con => _configureConnection?.Invoke(provider, con));
    }

    private NatsOpts GetNatsOpts(IServiceProvider provider)
    {
        var options = NatsOpts.Default with { LoggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance };
        options = _configureOpts?.Invoke(provider, options) ?? options;

        if (_serializerRegistry != null)
        {
            options = options with { SerializerRegistry = _serializerRegistry };
        }
        else
        {
            if (ReferenceEquals(options.SerializerRegistry, NatsOpts.Default.SerializerRegistry))
            {
                options = options with { SerializerRegistry = NatsClientDefaultSerializerRegistry.Default, };
            }
        }

        options = options with { SubPendingChannelFullMode = _pending ?? BoundedChannelFullMode.Wait };

        return options;
    }
}
