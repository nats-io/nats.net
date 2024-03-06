using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Extensions.Microsoft.DependencyInjection;

public class NatsBuilder
{
    private readonly IServiceCollection _services;
    private int _poolSize = 1;
    private Func<NatsOpts, NatsOpts>? _configureOpts;
    private Action<NatsConnection>? _configureConnection;
    private object? _diKey;

    public NatsBuilder(IServiceCollection services)
        => _services = services;

    public NatsBuilder WithPoolSize(int size)
    {
        _poolSize = Math.Max(size, 1);
        return this;
    }

    public NatsBuilder ConfigureOptions(Func<NatsOpts, NatsOpts> optsFactory)
    {
        var previousFactory = _configureOpts;
        _configureOpts = opts =>
        {
            // Apply the previous configurator if it exists.
            if (previousFactory != null)
            {
                opts = previousFactory(opts);
            }

            // Then apply the new configurator.
            return optsFactory(opts);
        };
        return this;
    }

    public NatsBuilder ConfigureConnection(Action<NatsConnection> connectionOpts)
    {
        _configureConnection = connectionOpts;
        return this;
    }

    public NatsBuilder AddJsonSerialization(JsonSerializerContext context)
        => ConfigureOptions(opts =>
        {
            var jsonRegistry = new NatsJsonContextSerializerRegistry(context);
            return opts with { SerializerRegistry = jsonRegistry };
        });

#if NET8_0_OR_GREATER
    public NatsBuilder WithKey(object key)
    {
        _diKey = key;
        return this;
    }
#endif

    public IServiceCollection Build()
    {
        if (_poolSize != 1)
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
        }
        else
        {
            if (_diKey == null)
            {
                _services.TryAddSingleton<NatsConnection>(provider => SingleConnectionFactory(provider));
                _services.TryAddSingleton<INatsConnection>(static provider => provider.GetRequiredService<NatsConnection>());
            }
            else
            {
#if NET8_0_OR_GREATER
                _services.TryAddKeyedSingleton(_diKey, SingleConnectionFactory);
                _services.TryAddKeyedSingleton<INatsConnection>(_diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnection>(key));
#endif
            }
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
        options = _configureOpts?.Invoke(options) ?? options;


        return new NatsConnectionPool(_poolSize, options, _configureConnection ?? (_ => { }));
    }

    private NatsConnection SingleConnectionFactory(IServiceProvider provider, object? diKey = null)
    {
        var options = NatsOpts.Default with { LoggerFactory = provider.GetRequiredService<ILoggerFactory>() };
        options = _configureOpts?.Invoke(options) ?? options;

        var conn = new NatsConnection(options);
        _configureConnection?.Invoke(conn);

        return conn;
    }
}
