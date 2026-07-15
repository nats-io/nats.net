using System.Diagnostics;

namespace NATS.Client.Core.Tests;

// NOTE: other test classes in this assembly may run concurrently with this one (xunit
// parallelizes across classes by default), but this class is the only one that reads or
// mutates the baggage-related members of NatsInstrumentationOptions.Default
// (PropagateBaggage / BaggageKeyFilter / BaggageSource), which is a process-global
// singleton. Every test here resets those three members before (constructor) and after
// (Dispose) it runs so state never leaks across tests, including into other [Fact]s in
// this same class which xunit runs as separate instances but not necessarily in parallel
// with each other by default.
//
// These tests call NATS.Client.Core.Internal.Telemetry.AddTraceContextHeaders directly
// (available via InternalsVisibleTo) and only assert on the "baggage" header. The
// trace-context headers (traceparent/tracestate) and, on .NET 8, the legacy
// DistributedContextPropagator's "Correlation-Context" header are also written by that
// call as a pre-existing side effect and are intentionally ignored here.
public class TelemetryBaggageTest : IDisposable
{
    public TelemetryBaggageTest() => ResetOptions();

    public void Dispose()
    {
        ResetOptions();
        Activity.Current = null;
    }

    [Fact]
    public void Disabled_by_default_does_not_add_baggage_header()
    {
        // Pin a no-output propagator: from DiagnosticSource 10 the DEFAULT propagator is the
        // W3C one, which writes activity baggage into a "baggage" header during Inject on its
        // own. Pinning isolates the assertion to what this feature writes (nothing, when off).
        var previousPropagator = DistributedContextPropagator.Current;
        DistributedContextPropagator.Current = DistributedContextPropagator.CreateNoOutputPropagator();

        var activity = StartActivity(("k", "v"));
        try
        {
            NatsHeaders? headers = null;
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers.Should().NotBeNull();
            headers!.ContainsKey(Telemetry.Constants.BaggageHeader).Should().BeFalse();
        }
        finally
        {
            activity.Stop();
            DistributedContextPropagator.Current = previousPropagator;
        }
    }

    [Fact]
    public void Enabled_round_trips_activity_baggage()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;

        var activity = StartActivity(("k1", "v1"), ("k2", "v2"));
        try
        {
            NatsHeaders? headers = null;
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers!.TryGetValue(Telemetry.Constants.BaggageHeader, out var values).Should().BeTrue();
            var tokens = values.ToString().Split(new[] { ", " }, StringSplitOptions.None);
            tokens.Should().BeEquivalentTo(new[] { "k1=v1", "k2=v2" });
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public void Enabled_encodes_values_needing_escaping()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;

        var activity = StartActivity(("k", "a b,c=d;e%f"));
        try
        {
            NatsHeaders? headers = null;
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers!.TryGetValue(Telemetry.Constants.BaggageHeader, out var values).Should().BeTrue();
            values.ToString().Should().Contain("k=a%20b%2Cc%3Dd%3Be%25f");
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public void Enabled_skips_null_value_entries()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;

        var activity = StartActivity(("nullkey", null), ("k", "v"));
        try
        {
            NatsHeaders? headers = null;
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers!.TryGetValue(Telemetry.Constants.BaggageHeader, out var values).Should().BeTrue();
            values.ToString().Should().Be("k=v");
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public void Enabled_removes_stale_header_when_all_entries_filtered()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;
        NatsInstrumentationOptions.Default.BaggageKeyFilter = _ => false;

        var activity = StartActivity(("k", "v"));
        try
        {
            var headers = new NatsHeaders { [Telemetry.Constants.BaggageHeader] = "stale=1" };
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers!.ContainsKey(Telemetry.Constants.BaggageHeader).Should().BeFalse();
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public void Enabled_leaves_headers_untouched_when_activity_has_no_baggage()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;

        var activity = StartActivity();
        try
        {
            var headers = new NatsHeaders { [Telemetry.Constants.BaggageHeader] = "app=1" };
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers!.TryGetValue(Telemetry.Constants.BaggageHeader, out var values).Should().BeTrue();
            values.ToString().Should().Be("app=1");
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public void Enabled_baggage_source_replaces_activity_baggage()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;
        NatsInstrumentationOptions.Default.BaggageSource = () => new[] { new KeyValuePair<string, string?>("src", "2") };

        var activity = StartActivity(("act", "1"));
        try
        {
            NatsHeaders? headers = null;
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers!.TryGetValue(Telemetry.Constants.BaggageHeader, out var values).Should().BeTrue();
            values.ToString().Should().Be("src=2");
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public void Enabled_baggage_source_returning_null_removes_header()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;
        NatsInstrumentationOptions.Default.BaggageSource = () => null!;

        // Pin a no-output propagator so the ambient (DiagnosticSource 10+ W3C) propagator
        // cannot overwrite the header during Inject; only this feature's behavior is under test.
        var previousPropagator = DistributedContextPropagator.Current;
        DistributedContextPropagator.Current = DistributedContextPropagator.CreateNoOutputPropagator();

        var activity = StartActivity(("act", "1"));
        try
        {
            // A configured BaggageSource is authoritative: when it yields no baggage for this
            // message, any pre-existing baggage header (app-set or ambient-written) must be
            // removed rather than leaked onto the wire.
            var headers = new NatsHeaders { [Telemetry.Constants.BaggageHeader] = "app=1" };
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers!.ContainsKey(Telemetry.Constants.BaggageHeader).Should().BeFalse();
        }
        finally
        {
            activity.Stop();
            DistributedContextPropagator.Current = previousPropagator;
        }
    }

    [Fact]
    public void Enabled_key_filter_allow_list_keeps_only_allowed_key()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;
        NatsInstrumentationOptions.Default.BaggageKeyFilter = key => key == "keep";

        var activity = StartActivity(("keep", "1"), ("drop", "2"));
        try
        {
            NatsHeaders? headers = null;
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            headers!.TryGetValue(Telemetry.Constants.BaggageHeader, out var values).Should().BeTrue();
            values.ToString().Should().Be("keep=1");
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public void Null_activity_is_a_no_op_even_when_enabled()
    {
        NatsInstrumentationOptions.Default.PropagateBaggage = true;

        NatsHeaders? nullHeaders = null;
        Telemetry.AddTraceContextHeaders(null, ref nullHeaders);
        nullHeaders.Should().BeNull();

        var preSetHeaders = new NatsHeaders { [Telemetry.Constants.BaggageHeader] = "app=1" };
        Telemetry.AddTraceContextHeaders(null, ref preSetHeaders);
        preSetHeaders!.TryGetValue(Telemetry.Constants.BaggageHeader, out var values).Should().BeTrue();
        values.ToString().Should().Be("app=1");
    }

    private static void ResetOptions()
    {
        var options = NatsInstrumentationOptions.Default;
        options.PropagateBaggage = false;
        options.BaggageKeyFilter = null;
        options.BaggageSource = null;
    }

    private static Activity StartActivity(params (string Key, string? Value)[] baggage)
    {
        var activity = new Activity("test");
        foreach (var (key, value) in baggage)
            activity.AddBaggage(key, value);
        activity.Start();
        return activity;
    }
}
