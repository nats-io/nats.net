using Microsoft.Extensions.DependencyInjection;

namespace NATS.Extensions.Microsoft.DependencyInjection;

public static class NatsHostingExtensions
{
    public static IServiceCollection AddNatsClient(this IServiceCollection services, Action<NatsBuilder>? buildAction = null)
    {
        var builder = new NatsBuilder(services);
        buildAction?.Invoke(builder);

        builder.Build();
        return services;
    }
}
