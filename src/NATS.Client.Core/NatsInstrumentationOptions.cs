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
    private volatile bool _propagateBaggage;
    private volatile Func<string, bool>? _baggageKeyFilter;
    private volatile Func<IEnumerable<KeyValuePair<string, string?>>>? _baggageSource;

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

    /// <summary>
    /// Gets or sets a value indicating whether W3C Baggage is propagated with messages.
    /// </summary>
    /// <remarks>
    /// When enabled, baggage is written to the message as a W3C <c>baggage</c> header on publish
    /// (sourced from <see cref="BaggageSource"/> when set, otherwise from the send activity's
    /// <see cref="Activity.Baggage"/>), and extracted from the <c>baggage</c> header and restored
    /// onto the receive activity on consume. Disabled by default because baggage can carry
    /// sensitive or high-cardinality data.
    /// </remarks>
    public bool PropagateBaggage
    {
        get => _propagateBaggage;
        set => _propagateBaggage = value;
    }

    /// <summary>
    /// Gets or sets a predicate that selects which baggage keys are propagated.
    /// </summary>
    /// <remarks>
    /// Applied on both publish (inject) and receive (extract). When null, all keys are propagated.
    /// Only used when <see cref="PropagateBaggage"/> is true. The predicate must not throw.
    /// </remarks>
    public Func<string, bool>? BaggageKeyFilter
    {
        get => _baggageKeyFilter;
        set => _baggageKeyFilter = value;
    }

    /// <summary>
    /// Gets or sets a callback that supplies the baggage entries to inject on publish.
    /// </summary>
    /// <remarks>
    /// When set (and <see cref="PropagateBaggage"/> is true), this replaces the send activity's
    /// <see cref="Activity.Baggage"/> as the source of injected baggage, allowing applications
    /// whose baggage lives elsewhere (for example OpenTelemetry's <c>Baggage.Current</c>) to
    /// propagate it. Entries with null values are skipped. Returning null or an empty sequence
    /// means no baggage. Only used on publish; the callback must not throw.
    /// </remarks>
    public Func<IEnumerable<KeyValuePair<string, string?>>>? BaggageSource
    {
        get => _baggageSource;
        set => _baggageSource = value;
    }
}
