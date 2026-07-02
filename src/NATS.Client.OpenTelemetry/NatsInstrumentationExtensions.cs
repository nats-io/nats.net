using System;
using NATS.Client.Core;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace NATS.Client.OpenTelemetry;

public static class NatsInstrumentationExtensions
{
    /// <summary>
    /// Adds the NATS .NET client <see cref="System.Diagnostics.ActivitySource"/> to the tracer provider,
    /// enabling distributed tracing for publish, subscribe, and request/reply operations.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to add the source to.</param>
    /// <returns>The supplied <paramref name="builder"/> for chaining.</returns>
    public static TracerProviderBuilder AddNatsClientInstrumentation(this TracerProviderBuilder builder) =>
        builder.AddSource(NatsTelemetry.SourceName);

    /// <summary>
    /// Adds the NATS .NET client <see cref="System.Diagnostics.ActivitySource"/> to the tracer provider and
    /// configures the shared <see cref="NatsInstrumentationOptions"/>.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to add the source to.</param>
    /// <param name="configure">Action that mutates the process-wide <see cref="NatsInstrumentationOptions.Default"/>.</param>
    /// <returns>The supplied <paramref name="builder"/> for chaining.</returns>
    public static TracerProviderBuilder AddNatsClientInstrumentation(this TracerProviderBuilder builder, Action<NatsInstrumentationOptions> configure)
    {
        configure?.Invoke(NatsInstrumentationOptions.Default);
        return builder.AddSource(NatsTelemetry.SourceName);
    }

    /// <summary>
    /// Adds the NATS .NET client <see cref="System.Diagnostics.Metrics.Meter"/> to the meter provider,
    /// enabling messaging metrics (published/consumed counters, operation duration, and more).
    /// </summary>
    public static MeterProviderBuilder AddNatsClientInstrumentation(this MeterProviderBuilder builder) =>
        builder.AddMeter(NatsTelemetry.SourceName);
}
