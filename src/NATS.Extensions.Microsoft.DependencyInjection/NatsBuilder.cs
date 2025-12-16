using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Net;

namespace NATS.Extensions.Microsoft.DependencyInjection;

public class NatsOptsBuilder
{
    public NatsOpts Opts { get; set; } = NatsOpts.Default;

    public Action<IServiceProvider, NatsConnection>? ConfigureConnection { get; set; }
}

public class NatsBuilder
{
    private readonly IServiceCollection _services;
    private OptionsBuilder<NatsOptsBuilder>? _builder;
    private Func<IServiceProvider, int> _poolSizeConfigurer = _ => 1;
    private object? _diKey = null;
    private string _optionsName = Options.DefaultName;

    public NatsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    private OptionsBuilder<NatsOptsBuilder> Builder => _builder ??= _services
        .AddOptions<NatsOptsBuilder>(_optionsName)
        .Configure<IServiceProvider>((opts, provider) =>
        {
            opts.Opts = opts.Opts with
            {
                LoggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                SubPendingChannelFullMode = BoundedChannelFullMode.Wait,
            };

            if (ReferenceEquals(opts.Opts.SerializerRegistry, NatsOpts.Default.SerializerRegistry))
            {
                opts.Opts = opts.Opts with { SerializerRegistry = NatsClientDefaultSerializerRegistry.Default, };
            }
        });

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

    public NatsBuilder ConfigureOptions(Action<OptionsBuilder<NatsOptsBuilder>> configure)
    {
        configure(Builder);

        return this;
    }

    [Obsolete("Use ConfigureOptions(Action<OptionsBuilder<NatsOptsBuilder>> configure) instead.")]
    public NatsBuilder ConfigureOptions(Func<NatsOpts, NatsOpts> optsFactory) =>
        ConfigureOptions(builder => builder.Configure(opts =>
            opts.Opts = optsFactory(opts.Opts)));

    [Obsolete("Use ConfigureOptions(Action<OptionsBuilder<NatsOptsBuilder>> configure) instead.")]
    public NatsBuilder ConfigureOptions(Func<IServiceProvider, NatsOpts, NatsOpts> optsFactory) =>
        ConfigureOptions(builder => builder.Configure<IServiceProvider>((opts, provider) =>
            opts.Opts = optsFactory(provider, opts.Opts)));

    public NatsBuilder ConfigureConnection(Action<IServiceProvider, NatsConnection> configureConnection) =>
        ConfigureOptions(builder =>
            builder.Configure<IServiceProvider>((opts, provider) =>
            {
                var configure = opts.ConfigureConnection;
                opts.ConfigureConnection = (serviceProvider, connection) =>
                {
                    configure?.Invoke(serviceProvider, connection);

                    configureConnection(serviceProvider, connection);
                };
            }));

    public NatsBuilder ConfigureConnection(Action<NatsConnection> configureConnection) =>
        ConfigureConnection((_, connection) => configureConnection(connection));

    [Obsolete("By default JSON serialization is enabled. If you still want to use generated JSON serialization, use WithSerializerRegistry, JsonSerializerContext and optionally NatsSerializerBuilder")]
    public NatsBuilder AddJsonSerialization(JsonSerializerContext context) =>
        AddJsonSerialization(_ => context);

    [Obsolete("By default JSON serialization is enabled. If you still want to use generated JSON serialization, use WithSerializerRegistry, JsonSerializerContext and optionally NatsSerializerBuilder")]
    public NatsBuilder AddJsonSerialization(Func<IServiceProvider, JsonSerializerContext> contextFactory) =>
        ConfigureOptions(builder =>
            builder.Configure<IServiceProvider>((opts, provider) =>
            {
                var context = contextFactory(provider);
                NatsJsonContextSerializerRegistry jsonRegistry = new(context);

                opts.Opts = opts.Opts with { SerializerRegistry = jsonRegistry };
            }));

#if NET8_0_OR_GREATER
    public NatsBuilder WithKey(object key)
    {
        if (_builder != null)
        {
            throw new InvalidOperationException("WithKey must be called before ConfigureOptions or other configuration methods.");
        }

        _diKey = key;
        _optionsName = key.ToString() ?? Options.DefaultName;

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
    public NatsBuilder WithSubPendingChannelFullMode(BoundedChannelFullMode pending) =>
        ConfigureOptions(builder => builder.Configure(opts => opts.Opts = opts.Opts with
        {
            SubPendingChannelFullMode = pending,
        }));

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
    public NatsBuilder WithSerializerRegistry(INatsSerializerRegistry registry) =>
        ConfigureOptions(builder => builder.Configure(opts => opts.Opts = opts.Opts with
        {
            SerializerRegistry = registry,
        }));

    internal IServiceCollection Build()
    {
        // Ensure the options builder is initialized to register the options infrastructure
        _ = Builder;

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
        var optionsName = diKey?.ToString() ?? Options.DefaultName;
        var options = provider.GetRequiredService<IOptionsMonitor<NatsOptsBuilder>>().Get(optionsName);

        return new NatsConnectionPool(_poolSizeConfigurer(provider), options.Opts, con => options.ConfigureConnection?.Invoke(provider, con));
    }
}
