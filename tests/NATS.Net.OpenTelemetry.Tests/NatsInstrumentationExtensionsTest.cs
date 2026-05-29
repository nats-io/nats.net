using NATS.Client.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace NATS.Client.Core.Tests;

public class NatsInstrumentationExtensionsTest
{
    [Fact]
    public void AddNatsClientInstrumentation_builds_tracer_provider()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddNatsClientInstrumentation()
            .Build();

        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddNatsClientInstrumentation_builds_meter_provider()
    {
        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddNatsClientInstrumentation()
            .Build();

        provider.Should().NotBeNull();
    }

    [Fact]
    public void SourceName_is_NATS_Net()
    {
        NatsTelemetry.SourceName.Should().Be("NATS.Net");
    }
}
