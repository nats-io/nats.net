using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Extensions.Microsoft.DependencyInjection;

public static class NatsHostingExtensions
{
    public static IServiceCollection AddNats(this IServiceCollection services, Action<NatsBuilder>? buildAction = null)
    {
        var builder = new NatsBuilder(services);
        buildAction?.Invoke(builder);

        builder.Build();
        return services;
    }
}
