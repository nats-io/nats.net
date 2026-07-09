using Microsoft.Extensions.DependencyInjection;

namespace NATS.Extensions.Microsoft.DependencyInjection;

public static class NatsHostingExtensions
{
    /// <summary>
    /// Registers a NATS client on the service collection with ad hoc JSON serialization enabled by default.
    /// </summary>
    /// <param name="services">The service collection to add NATS to.</param>
    /// <param name="buildAction">Optional callback to configure the client through <see cref="NatsBuilder"/>.</param>
    /// <returns>The same service collection so calls can be chained.</returns>
    /// <remarks>
    /// Registers <c>INatsClient</c>, <c>INatsConnection</c>, <c>NatsConnection</c> and the connection pool.
    /// This is the option to use for most applications. For AOT deployments or a minimal dependency footprint
    /// without the JSON serializer, use <c>NATS.Client.Hosting</c> and its <c>AddNats</c> method instead.
    /// </remarks>
    public static IServiceCollection AddNatsClient(this IServiceCollection services, Action<NatsBuilder>? buildAction = null)
    {
        var builder = new NatsBuilder(services);
        buildAction?.Invoke(builder);

        builder.Build();
        return services;
    }
}
