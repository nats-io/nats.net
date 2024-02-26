using System.Reflection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Example.Core;

public static class TracingSetup
{
    public static void SetSandboxEnv()
    {
        var instanceId = Guid.NewGuid().ToString();
        var assemblyName = Assembly.GetEntryAssembly()!.GetName().Name;
        Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", assemblyName);
        Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", $"service.instance.id={instanceId}");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:16023");    // set to an OTLP endpoint
    }

    public static TracerProvider RunSandboxTracing(bool console = false, bool internalTraces = false)
    {
        SetSandboxEnv();
        return new TracerProviderBuilderBase()
                   .ConfigureResource(o => o.AddTelemetrySdk())
                   .AddNatsInstrumentation(includeInternal: internalTraces)
                   .MaybeAddInternalSource(internalTraces)
                   .AddOtlpExporter()
                   .MaybeAddConsoleExporter(console)
                   .Build()
               ?? throw new Exception("Tracer provider build returned null.");
    }

    private static TracerProviderBuilder MaybeAddConsoleExporter(this TracerProviderBuilder builder, bool console)
        => console ? builder.AddConsoleExporter() : builder;

    private static TracerProviderBuilder MaybeAddInternalSource(this TracerProviderBuilder builder, bool internalTraces)
        => internalTraces ? builder.AddSource("NATS.Client.Internal") : builder;
}
