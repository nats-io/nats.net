using System.Diagnostics;

namespace NATS.Client.Core;

/// <summary>
/// Options for the OpenTelemetry instrumentation.
/// </summary>
public sealed class NatsInstrumentationOptions
{
    // The shared Default instance is configured at startup but its callbacks are read on the
    // publish/subscribe path from other threads. Backing fields are volatile so a configuration
    // change made after traffic has started is observed by those readers.
    private volatile Func<NatsInstrumentationContext, bool>? _filter;
    private volatile Action<Activity, NatsInstrumentationContext>? _enrich;
    private volatile Func<string, string>? _spanDestinationNameFormatter;

    public static NatsInstrumentationOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets a filter function that determines whether or not to collect telemetry on a per request basis.
    /// </summary>
    /// <remarks>
    /// The return value for the filter function is interpreted as follows:
    /// - If filter returns `true`, the request is collected.
    /// - If filter returns `false` or throws an exception the request is NOT collected.
    /// </remarks>
    public Func<NatsInstrumentationContext, bool>? Filter
    {
        get => _filter;
        set => _filter = value;
    }

    /// <summary>
    /// Gets or sets an action to enrich an Activity.
    /// </summary>
    public Action<Activity, NatsInstrumentationContext>? Enrich
    {
        get => _enrich;
        set => _enrich = value;
    }

    /// <summary>
    /// Gets or sets a function that formats the destination name used in span names.
    /// </summary>
    /// <remarks>
    /// The input is the raw NATS subject. This only changes activity names, not telemetry tags.
    /// </remarks>
    public Func<string, string>? SpanDestinationNameFormatter
    {
        get => _spanDestinationNameFormatter;
        set => _spanDestinationNameFormatter = value;
    }
}
