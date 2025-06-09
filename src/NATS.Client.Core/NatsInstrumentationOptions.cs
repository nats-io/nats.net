using System.Diagnostics;

namespace NATS.Client.Core;

/// <summary>
/// Options for the OpenTelemetry instrumentation.
/// </summary>
public sealed class NatsInstrumentationOptions
{
    public static NatsInstrumentationOptions Default => new();

    /// <summary>
    /// Gets or sets a filter function that determines whether or not to collect telemetry on a per request basis.
    /// </summary>
    /// <remarks>
    /// The return value for the filter function is interpreted as follows:
    /// - If filter returns `true`, the request is collected.
    /// - If filter returns `false` or throws an exception the request is NOT collected.
    /// </remarks>
    public Func<NatsInstrumentationContext, bool>? Filter { get; set; }

    /// <summary>
    /// Gets or sets an action to enrich an Activity.
    /// </summary>
    public Action<Activity, NatsInstrumentationContext>? Enrich { get; set; }
}
