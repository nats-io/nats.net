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
    public static TracerProviderBuilder AddNatsClientInstrumentation(this TracerProviderBuilder builder) =>
        builder.AddSource(NatsTelemetry.SourceName);

    /// <summary>
    /// Adds the NATS .NET client <see cref="System.Diagnostics.Metrics.Meter"/> to the meter provider,
    /// enabling messaging metrics (published/consumed counters, operation duration, and more).
    /// </summary>
    public static MeterProviderBuilder AddNatsClientInstrumentation(this MeterProviderBuilder builder) =>
        builder.AddMeter(NatsTelemetry.SourceName);
}
