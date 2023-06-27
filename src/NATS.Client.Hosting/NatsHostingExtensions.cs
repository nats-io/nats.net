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
    public static IServiceCollection AddNats(this IServiceCollection services, int poolSize = 1, Func<NatsOptions, NatsOptions>? configureOptions = null, Action<NatsConnection>? configureConnection = null)
    {
        poolSize = Math.Max(poolSize, 1);

        if (poolSize != 1)
        {
            services.TryAddSingleton<NatsConnectionPool>(provider =>
            {
                var options = NatsOptions.Default with { LoggerFactory = provider.GetRequiredService<ILoggerFactory>() };
                if (configureOptions != null)
                {
                    options = configureOptions(options);
                }

                return new NatsConnectionPool(poolSize, options, configureConnection ?? (_ => { }));
            });

            services.TryAddTransient<NatsConnection>(static provider =>
            {
                var pool = provider.GetRequiredService<NatsConnectionPool>();
                return pool.GetConnection();
            });

            services.TryAddTransient<INatsConnection>(static provider =>
            {
                var pool = provider.GetRequiredService<NatsConnectionPool>();
                return pool.GetCommand();
            });
        }
        else
        {
            services.TryAddSingleton<NatsConnection>(provider =>
            {
                var options = NatsOptions.Default with { LoggerFactory = provider.GetRequiredService<ILoggerFactory>() };
                if (configureOptions != null)
                {
                    options = configureOptions(options);
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
