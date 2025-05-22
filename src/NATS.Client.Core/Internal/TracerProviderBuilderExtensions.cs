using OpenTelemetry.Trace;

namespace NATS.Client.Core.Internal;

public static class TracerProviderBuilderExtensions
{
    public static TracerProviderBuilder AddNatsInstrumentation(
        this TracerProviderBuilder builder,
        Action<NatsInstrumentationOptions>? configure = null)
    {
        if (configure is not null)
            configure(NatsInstrumentationOptions.Default);

        builder.AddSource(Telemetry.NatsActivitySource);
        return builder;
    }
}
