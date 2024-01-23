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
    public static IServiceCollection AddNats(
        this IServiceCollection services,
        int poolSize = 1,
        Func<NatsOpts, NatsOpts>? configureOpts = null,
        Action<NatsConnection>? configureConnection = null
#if NET8_0_OR_GREATER
        , string? key = null // This parameter is only available in .NET 8 or greater
#endif
    )
    {
        string? diKey = null;
#if NET8_0_OR_GREATER
        diKey = key;
#endif

        poolSize = Math.Max(poolSize, 1);

        if (poolSize != 1)
        {
            services.TryAddSingleton<NatsConnectionPool>(provider =>
            {
                var options = NatsOpts.Default with { LoggerFactory = provider.GetRequiredService<ILoggerFactory>() };
                if (configureOpts != null)
                {
                    options = configureOpts(options);
                }

                return new NatsConnectionPool(poolSize, options, configureConnection ?? (_ => { }));
            });

            services.TryAddSingleton<INatsConnectionPool>(static provider => provider.GetRequiredService<NatsConnectionPool>());
            services.TryAddTransient<NatsConnection>(static provider =>
            {
                var pool = provider.GetRequiredService<NatsConnectionPool>();
                return (pool.GetConnection() as NatsConnection)!;
            });

            if (string.IsNullOrEmpty(diKey))
            {
                services.TryAddTransient<INatsConnection>(static provider => provider.GetRequiredService<NatsConnection>());
            }
            else
            {
#if NET8_0_OR_GREATER
                services.AddKeyedTransient<INatsConnection>(diKey, static (provider, _) => provider.GetRequiredService<NatsConnection>());
#endif
            }
        }
        else
        {
            services.TryAddSingleton<NatsConnection>(provider =>
            {
                var options = NatsOpts.Default with { LoggerFactory = provider.GetRequiredService<ILoggerFactory>() };
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
            });

            if (string.IsNullOrEmpty(diKey))
            {
                services.TryAddSingleton<INatsConnection>(static provider => provider.GetRequiredService<NatsConnection>());
            }
            else
            {
#if NET8_0_OR_GREATER
                services.AddKeyedSingleton<INatsConnection>(diKey, static (provider, _) => provider.GetRequiredService<NatsConnection>());
#endif
            }
        }

        return services;
    }
}
