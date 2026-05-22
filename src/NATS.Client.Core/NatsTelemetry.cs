namespace NATS.Client.Core;

/// <summary>
/// Telemetry identifiers for NATS .NET. Use these when configuring OpenTelemetry
/// or any other listener directly. The same name is used for both the
/// <see cref="System.Diagnostics.ActivitySource"/> and the
/// <see cref="System.Diagnostics.Metrics.Meter"/>.
/// </summary>
public static class NatsTelemetry
{
    public const string SourceName = "NATS.Net";
}
