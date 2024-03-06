using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Client.Hosting;

public static class NatsHostingExtensions
{
    /// <summary>
    /// Add NatsConnection/Pool to ServiceCollection. When poolSize = 1, registered `NatsConnection` and `INatsConnection` as singleton.
    /// Others, registered `NatsConnectionPool` as singleton, `NatsConnection` and `INatsConnection` as transient(get from pool).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.SpacingRules", "SA1001:Commas should not be preceded by whitespace", Justification = "Required for conditional build.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.SpacingRules", "SA1009:Closing parenthesis should not be preceded by a space", Justification = "Required for conditional build.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1111:Closing parenthesis should be on the same line as the last parameter", Justification = "Required for conditional build.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1113:Comma should be on the same line as previous parameter", Justification = "Required for conditional build.")]
    [Obsolete("This package is obsolete. Use NATS.Extensions.Microsoft.DependencyInjection instead.")]
    public static IServiceCollection AddNats(
        this IServiceCollection services,
        int poolSize = 1,
        Func<NatsOpts, NatsOpts>? configureOpts = null,
        Action<NatsConnection>? configureConnection = null
#if NET8_0_OR_GREATER
        , object? key = null // This parameter is only available in .NET 8 or greater
#endif
    )
    {
        object? diKey = null;
#if NET8_0_OR_GREATER
        diKey = key;
#endif

        poolSize = Math.Max(poolSize, 1);

        if (poolSize != 1)
        {
            NatsConnectionPool PoolFactory(IServiceProvider provider)
            {
                var options = NatsOpts.Default with
                {
                    LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
                };
                if (configureOpts != null)
                {
                    options = configureOpts(options);
                }

                return new NatsConnectionPool(poolSize, options, configureConnection ?? (_ => { }));
            }

            static NatsConnection ConnectionFactory(IServiceProvider provider, object? key)
            {
#if NET8_0_OR_GREATER
                if (key == null)
                {
                    var pool = provider.GetRequiredService<NatsConnectionPool>();
                    return (pool.GetConnection() as NatsConnection)!;
                }
                else
                {
                    var pool = provider.GetRequiredKeyedService<NatsConnectionPool>(key);
                    return (pool.GetConnection() as NatsConnection)!;
                }
#else
                var pool = provider.GetRequiredService<NatsConnectionPool>();
                return (pool.GetConnection() as NatsConnection)!;
#endif
            }

            if (diKey == null)
            {
                services.TryAddSingleton(PoolFactory);
                services.TryAddSingleton<INatsConnectionPool>(static provider => provider.GetRequiredService<NatsConnectionPool>());
                services.TryAddTransient<NatsConnection>(static provider => ConnectionFactory(provider, null));
                services.TryAddTransient<INatsConnection>(static provider => provider.GetRequiredService<NatsConnection>());
            }
            else
            {
#if NET8_0_OR_GREATER
                services.TryAddKeyedSingleton(diKey, (provider, _) => PoolFactory(provider));
                services.TryAddKeyedSingleton<INatsConnectionPool>(diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnectionPool>(key));
                services.TryAddKeyedTransient<NatsConnection>(diKey, static (provider, key) => ConnectionFactory(provider, key));
                services.TryAddKeyedTransient<INatsConnection>(diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnection>(key));
#endif
            }
        }
        else
        {
            NatsConnection Factory(IServiceProvider provider)
            {
                var options = NatsOpts.Default with
                {
                    LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
                };
                if (configureOpts != null)
                {
                    options = configureOpts(options);
                }

                var conn = new NatsConnection(options);
                if (configureConnection != null)
                {
                    configureConnection(conn);
                }

                return conn;
            }

            if (diKey == null)
            {
                services.TryAddSingleton(Factory);
                services.TryAddSingleton<INatsConnection>(static provider => provider.GetRequiredService<NatsConnection>());
            }
            else
            {
#if NET8_0_OR_GREATER
                services.TryAddKeyedSingleton<NatsConnection>(diKey, (provider, _) => Factory(provider));
                services.TryAddKeyedSingleton<INatsConnection>(diKey, static (provider, key) => provider.GetRequiredKeyedService<NatsConnection>(key));
#endif
            }
        }

        return services;
    }
}
