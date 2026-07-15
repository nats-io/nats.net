using System.Diagnostics;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

// Tests for opt-in W3C Baggage propagation (issue #1200).
//
// These tests mutate NatsInstrumentationOptions.Default, which is a process-global
// singleton. xUnit serializes tests WITHIN a single class, so keeping every baggage
// test in this one class prevents them from racing on that shared state. Each test
// MUST restore the global options in a finally block (see ResetOptions) so a failure
// in one test cannot leak configuration into another.
//
// xUnit still runs test CLASSES in parallel, and both the ActivityListener (see
// ActivityTracker) and the instrumentation options are process-global. So a receive
// activity from a concurrently running test class can appear in this test's tracker,
// and this test's global Enrich callback fires for those foreign activities too. Every
// activity/enrich selection here is therefore keyed on the test's unique subject rather
// than on ActivityKind alone (see FindReceiveActivity and the Enrich guards).
//
// Note: independent of this feature, the ambient DistributedContextPropagator writes
// activity baggage during trace-context injection -- as "Correlation-Context" with
// the legacy propagator (DiagnosticSource <= 9, the default there) or as a W3C
// "baggage" header with the W3C propagator (the DEFAULT in DiagnosticSource 10+,
// which this test app resolves via the OpenTelemetry SDK). So with the feature
// disabled a "baggage" header can still legitimately appear on the wire; these tests
// only assert on behavior this feature owns: send-side filtering/override when
// enabled, and extraction/restoration on receive (which never happens when disabled).
//
// Shares a collection with NatsInstrumentationExtensionsTest: both mutate the
// process-global NatsInstrumentationOptions.Default and would race if xunit ran
// the classes in parallel.
[Collection("nats-instrumentation-options")]
public class OpenTelemetryBaggageTest
{
    [Fact]
    public async Task Baggage_not_propagated_by_default()
    {
        try
        {
            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            const string subject = "baggage.default";

            await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

            var headers = new NatsHeaders();
            using (var ambient = new Activity("ambient"))
            {
                ambient.AddBaggage("user.id", "alice").AddBaggage("tenant", "acme");
                ambient.Start();
                await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
            }

            await sub.Msgs.ReadAsync(cts.Token);

            // Feature disabled: nothing is extracted or restored on the receive side,
            // even though the ambient (DiagnosticSource 10+ W3C) propagator may have
            // written a "baggage" header on publish independent of this feature.
            var receive = FindReceiveActivity(tracker, subject);
            receive.GetBaggageItem("user.id").Should().BeNull();
            receive.GetBaggageItem("tenant").Should().BeNull();
        }
        finally
        {
            ResetOptions();
        }
    }

    [Fact]
    public async Task Baggage_round_trips_when_enabled()
    {
        try
        {
            NatsInstrumentationOptions.Default.PropagateBaggage = true;

            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            const string subject = "baggage.roundtrip";

            await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

            var headers = new NatsHeaders();
            using (var ambient = new Activity("ambient"))
            {
                ambient.AddBaggage("user.id", "alice").AddBaggage("tenant", "acme");
                ambient.Start();
                await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
            }

            await sub.Msgs.ReadAsync(cts.Token);

            headers.ContainsKey("baggage").Should().BeTrue();
            headers["baggage"].ToString().Should().Contain("user.id=alice");

            var receive = FindReceiveActivity(tracker, subject);
            receive.GetBaggageItem("user.id").Should().Be("alice");
            receive.GetBaggageItem("tenant").Should().Be("acme");
        }
        finally
        {
            ResetOptions();
        }
    }

    [Fact]
    public async Task Baggage_key_filter_limits_injected_keys()
    {
        try
        {
            NatsInstrumentationOptions.Default.PropagateBaggage = true;
            NatsInstrumentationOptions.Default.BaggageKeyFilter = k => k == "tenant";

            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            const string subject = "baggage.filter.inject";

            await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

            var headers = new NatsHeaders();
            using (var ambient = new Activity("ambient"))
            {
                ambient.AddBaggage("user.id", "alice").AddBaggage("tenant", "acme");
                ambient.Start();
                await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
            }

            await sub.Msgs.ReadAsync(cts.Token);

            var raw = headers["baggage"].ToString();
            raw.Should().Contain("tenant=acme");
            raw.Should().NotContain("user.id");

            var receive = FindReceiveActivity(tracker, subject);
            receive.GetBaggageItem("tenant").Should().Be("acme");
            receive.GetBaggageItem("user.id").Should().BeNull();
        }
        finally
        {
            ResetOptions();
        }
    }

    [Fact]
    public async Task Baggage_key_filter_applies_on_extract()
    {
        try
        {
            // Unique per test: guard Enrich by subject because the global Enrich also fires
            // for activities from test classes running concurrently.
            const string subject = "baggage.filter.extract";
            IReadOnlyList<KeyValuePair<string, string?>>? capturedBaggage = null;

            NatsInstrumentationOptions.Default.PropagateBaggage = true;
            NatsInstrumentationOptions.Default.BaggageKeyFilter = k => k == "tenant";
            NatsInstrumentationOptions.Default.Enrich = (activity, context) =>
            {
                if (activity.Kind == ActivityKind.Consumer && context.Subject == subject)
                    capturedBaggage = context.Baggage;
            };

            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

            // No ambient activity is started, so the send activity has no baggage of its own.
            // The application sets a "baggage" header directly; because the feature only
            // touches the header when the send activity/source HAS baggage, this app-set
            // header must be preserved and delivered as-is.
            var headers = new NatsHeaders
            {
                ["baggage"] = "user.id=alice, tenant=acme",
            };
            await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);

            // The app-supplied header survived publish untouched.
            headers["baggage"].ToString().Should().Be("user.id=alice, tenant=acme");

            await sub.Msgs.ReadAsync(cts.Token);

            var receive = FindReceiveActivity(tracker, subject);
            receive.GetBaggageItem("tenant").Should().Be("acme");
            receive.GetBaggageItem("user.id").Should().BeNull();

            capturedBaggage.Should().NotBeNull();
            capturedBaggage!.Should().ContainSingle();
            capturedBaggage!.Single().Key.Should().Be("tenant");
            capturedBaggage!.Single().Value.Should().Be("acme");
        }
        finally
        {
            ResetOptions();
        }
    }

    [Fact]
    public async Task Baggage_values_are_w3c_encoded()
    {
        try
        {
            const string originalValue = "a b,c=d;e%f";

            NatsInstrumentationOptions.Default.PropagateBaggage = true;

            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            const string subject = "baggage.encoding";

            await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

            var headers = new NatsHeaders();
            using (var ambient = new Activity("ambient"))
            {
                ambient.AddBaggage("data", originalValue);
                ambient.Start();
                await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
            }

            await sub.Msgs.ReadAsync(cts.Token);

            // A single baggage entry -> no ", " list separator, so the whole value token
            // is the percent-encoded value after the first '='.
            var raw = headers["baggage"].ToString();
            var valueToken = raw.Substring(raw.IndexOf('=') + 1);
            valueToken.Should().NotContain(" ");
            valueToken.Should().NotContain(",");
            valueToken.Should().NotContain(";");
            valueToken.Should().NotContain("=");

            var receive = FindReceiveActivity(tracker, subject);
            receive.GetBaggageItem("data").Should().Be(originalValue);
        }
        finally
        {
            ResetOptions();
        }
    }

    [Fact]
    public async Task Baggage_exposed_on_enrich_context()
    {
        try
        {
            // Unique per test: guard Enrich by subject because the global Enrich also fires
            // for activities from test classes running concurrently.
            const string subject = "baggage.enrich";
            IReadOnlyList<KeyValuePair<string, string?>>? consumerBaggage = null;
            var sawProducer = false;
            var producerBaggageWasNull = false;

            NatsInstrumentationOptions.Default.PropagateBaggage = true;
            NatsInstrumentationOptions.Default.Enrich = (activity, context) =>
            {
                if (context.Subject != subject)
                    return;

                if (activity.Kind == ActivityKind.Producer)
                {
                    sawProducer = true;
                    producerBaggageWasNull = context.Baggage is null;
                }

                if (activity.Kind == ActivityKind.Consumer)
                {
                    consumerBaggage = context.Baggage;
                }
            };

            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

            var headers = new NatsHeaders();
            using (var ambient = new Activity("ambient"))
            {
                ambient.AddBaggage("user.id", "alice").AddBaggage("tenant", "acme");
                ambient.Start();
                await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
            }

            await sub.Msgs.ReadAsync(cts.Token);

            sawProducer.Should().BeTrue();
            producerBaggageWasNull.Should().BeTrue();

            consumerBaggage.Should().NotBeNull();
            consumerBaggage!.Should().HaveCount(2);
            consumerBaggage!.Should().Contain(kv => kv.Key == "user.id" && kv.Value == "alice");
            consumerBaggage!.Should().Contain(kv => kv.Key == "tenant" && kv.Value == "acme");
        }
        finally
        {
            ResetOptions();
        }
    }

    [Fact]
    public async Task Filter_excluding_all_keys_removes_baggage_header()
    {
        try
        {
            NatsInstrumentationOptions.Default.PropagateBaggage = true;
            NatsInstrumentationOptions.Default.BaggageKeyFilter = _ => false;

            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            const string subject = "baggage.filter.all";

            await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

            var headers = new NatsHeaders();
            using (var ambient = new Activity("ambient"))
            {
                ambient.AddBaggage("user.id", "alice").AddBaggage("tenant", "acme");
                ambient.Start();
                await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
            }

            await sub.Msgs.ReadAsync(cts.Token);

            // Source had baggage but the filter rejected everything: the "baggage" header
            // is removed rather than left with unfiltered content.
            headers.ContainsKey("baggage").Should().BeFalse();
        }
        finally
        {
            ResetOptions();
        }
    }

    [Fact]
    public async Task Baggage_source_callback_overrides_activity_baggage()
    {
        try
        {
            NatsInstrumentationOptions.Default.PropagateBaggage = true;
            NatsInstrumentationOptions.Default.BaggageSource = () => new[]
            {
                new KeyValuePair<string, string?>("src.key", "src-val"),
            };

            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            const string subject = "baggage.source";

            await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

            var headers = new NatsHeaders();
            using (var ambient = new Activity("ambient"))
            {
                ambient.AddBaggage("user.id", "alice").AddBaggage("tenant", "acme");
                ambient.Start();
                await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
            }

            await sub.Msgs.ReadAsync(cts.Token);

            var raw = headers["baggage"].ToString();
            raw.Should().Contain("src.key=src-val");
            raw.Should().NotContain("user.id");

            var receive = FindReceiveActivity(tracker, subject);
            receive.GetBaggageItem("src.key").Should().Be("src-val");
            receive.GetBaggageItem("user.id").Should().BeNull();
        }
        finally
        {
            ResetOptions();
        }
    }

    [Fact]
    public async Task Child_activity_copies_baggage_when_enabled()
    {
        try
        {
            using var tracker = new ActivityTracker();
            await using var server = await NatsServerProcess.StartAsync();
            await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Part 1: feature enabled -> child activity inherits the receive activity's baggage.
            {
                NatsInstrumentationOptions.Default.PropagateBaggage = true;

                const string subject = "baggage.child.on";
                await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

                var headers = new NatsHeaders();
                using (var ambient = new Activity("ambient"))
                {
                    ambient.AddBaggage("user.id", "alice");
                    ambient.Start();
                    await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
                }

                var msg = await sub.Msgs.ReadAsync(cts.Token);
                using var child = msg.StartActivity("process");
                child.Should().NotBeNull();
                child!.GetBaggageItem("user.id").Should().Be("alice");
            }

            // Part 2: feature disabled -> child activity does not inherit baggage.
            {
                NatsInstrumentationOptions.Default.PropagateBaggage = false;

                const string subject = "baggage.child.off";
                await using var sub = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cts.Token);

                var headers = new NatsHeaders();
                using (var ambient = new Activity("ambient"))
                {
                    ambient.AddBaggage("user.id", "alice");
                    ambient.Start();
                    await nats.PublishAsync(subject, "bar", headers: headers, cancellationToken: cts.Token);
                }

                var msg = await sub.Msgs.ReadAsync(cts.Token);
                using var child = msg.StartActivity("process");
                child.Should().NotBeNull();
                child!.GetBaggageItem("user.id").Should().BeNull();
            }
        }
        finally
        {
            ResetOptions();
        }
    }

    // The tracker's ActivityListener is process-wide, and xUnit runs test classes in
    // parallel, so tracker.Started can contain receive activities produced by other test
    // classes running concurrently. Match on the full-subject tag (unique per test) rather
    // than on ActivityKind or the truncated activity name to select this test's activity.
    private Activity FindReceiveActivity(ActivityTracker tracker, string subject) =>
        tracker.Started.First(x =>
            x.Kind == ActivityKind.Consumer &&
            (string?)x.GetTagItem("messaging.nats.message.subject") == subject);

    private void ResetOptions()
    {
        var options = NatsInstrumentationOptions.Default;
        options.PropagateBaggage = false;
        options.BaggageKeyFilter = null;
        options.BaggageSource = null;
        options.Enrich = null;
        options.Filter = null;
    }
}
