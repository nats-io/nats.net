using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Extensions.Microsoft.DependencyInjection;

public class NatsBuilder
{
    private readonly IServiceCollection _services;

    private Func<IServiceProvider, int> _poolSizeConfigurer = _ => 1;
    private Func<IServiceProvider, NatsOpts, NatsOpts>? _configureOpts;
    private Action<IServiceProvider, NatsConnection>? _configureConnection;
    private object? _diKey = null;

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

    public NatsBuilder AddJsonSerialization(JsonSerializerContext context) =>
        AddJsonSerialization(_ => context);

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

    internal IServiceCollection Build()
    {
        if (_diKey == null)
        {
            _services.TryAddSingleton<NatsConnectionPool>(provider => PoolFactory(provider));
            _services.TryAddSingleton<INatsConnectionPool>(static provider => provider.GetRequiredService<NatsConnectionPool>());
            _services.TryAddTransient<NatsConnection>(static provider => PooledConnectionFactory(provider, null));
            _services.TryAddTransient<INatsConnection>(static provider => provider.GetRequiredService<NatsConnection>());
        }
        else
        {
#if NET8_0_OR_GREATER
            _services.TryAddKeyedSingleton<NatsConnectionPool>(_diKey, PoolFactory);
            _services.TryAddKeyedSingleton<INatsConnectionPool>(_diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnectionPool>(key));
            _services.TryAddKeyedTransient(_diKey, PooledConnectionFactory);
            _services.TryAddKeyedTransient<INatsConnection>(_diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnection>(key));
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
        var options = NatsOpts.Default with { LoggerFactory = provider.GetRequiredService<ILoggerFactory>() };
        options = _configureOpts?.Invoke(provider, options) ?? options;

        return new NatsConnectionPool(_poolSizeConfigurer(provider), options, con => _configureConnection?.Invoke(provider, con));
    }
}
