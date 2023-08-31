using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Client.Hosting;

public static class NatsHostingExtensions
{
    /// <summary>
    /// Add NatsConnection/Pool to ServiceCollection. When poolSize = 1, registered `NatsConnection` and `INatsCommand` as singleton.
    /// Others, registered `NatsConnectionPool` as singleton, `NatsConnection` and `INatsCommand` as transient(get from pool).
    /// </summary>
    public static IServiceCollection AddNats(this IServiceCollection services, int poolSize = 1, Func<NatsOpts, NatsOpts>? configureOpts = null, Action<NatsConnection>? configureConnection = null)
    {
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

            services.TryAddSingleton<INatsConnectionPool>(static provider =>
            {
                return provider.GetRequiredService<NatsConnectionPool>();
            });

            services.TryAddTransient<NatsConnection>(static provider =>
            {
                var pool = provider.GetRequiredService<NatsConnectionPool>();
                return (pool.GetConnection() as NatsConnection)!;
            });

            services.TryAddTransient<INatsConnection>(static provider =>
            {
                return provider.GetRequiredService<NatsConnection>();
            });
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

            services.TryAddSingleton<INatsConnection>(static provider =>
            {
                return provider.GetRequiredService<NatsConnection>();
            });
        }

        return services;
    }
}
