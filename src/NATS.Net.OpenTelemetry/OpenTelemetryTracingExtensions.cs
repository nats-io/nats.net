using NATS.Client.Core;

// ReSharper disable once CheckNamespace - Place in OTEL namespace for discoverability
namespace OpenTelemetry.Trace;

public static class OpenTelemetryTracingExtensions
{
    /// <summary>
    /// Enables trace collection on activity sources from the NATS.Client nuget package.
    /// </summary>
    /// <param name="builder"><see cref="TracerProviderBuilder"/> being configured.</param>
    /// <param name="includeInternal">Include traces from internal messaging used for control flow and lower level usage during high level abstractions (e.g. JetStream, KvStore, etc)</param>
    /// <returns>The same instance of <see cref="TracerProviderBuilder"/> to chain the calls.</returns>
    public static TracerProviderBuilder AddNatsInstrumentation(this TracerProviderBuilder builder, bool includeInternal = false)
    {
        builder.AddSource("NATS.Client");

        if (includeInternal)
            builder.AddSource("NATS.Client.Internal");

        return builder;
    }
}
