using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.CoreUnit.Tests;

public class NatsConnectionTelemetryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("_INBOX.reply")]
    public async Task StartReceiveActivity_fallback_emits_no_null_key_tag(string? replyTo)
    {
        // A freshly constructed connection has no ServerInfo, so StartReceiveActivity
        // takes the fallback branch. That branch must size its tag array to the tags it
        // actually sets, or a trailing default {null, null} entry leaks into the activity.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.NatsActivitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        await using var nats = new NatsConnection();

        using var activity = Telemetry.StartReceiveActivity(
            nats,
            name: "receive",
            subscriptionSubject: "foo.bar",
            queueGroup: null,
            subject: "foo.bar",
            replyTo: replyTo,
            bodySize: 0,
            size: 0,
            headers: null);

        activity.Should().NotBeNull();
        activity!.TagObjects.Should().OnlyContain(tag => tag.Key != null);

        var hasReplyToTag = activity.TagObjects.Any(tag => tag.Key == Telemetry.Constants.ReplyTo);
        hasReplyToTag.Should().Be(replyTo is not null);
    }

    [Theory]
    [InlineData("foo", "foo")]
    [InlineData("foo.bar", "foo.bar")]
    [InlineData("foo.bar.baz", "foo.bar")]
    [InlineData("foo.bar.baz.qux", "foo.bar")]
    [InlineData("foo.", "foo.")]
    [InlineData("foo..bar", "foo.")]
    [InlineData(".foo.bar", ".foo")]
    [InlineData("..", ".")]
    public async Task SpanDestinationName_collapses_to_first_two_subject_tokens(string subject, string expected)
    {
        await using var nats = new NatsConnection();

        nats.SpanDestinationName(subject).Should().Be(expected);
    }

    [Fact]
    public async Task StartReceiveActivity_leaves_ambient_current_untouched_and_does_not_parent_to_it()
    {
        // Receive activities are started on the connection's read loop, where
        // Activity.Current is whatever ran last on that execution context. Parenting to
        // it would chain unrelated messages together and keep the chain reachable
        // through the read loop's AsyncLocal, and leaving the new activity in Current
        // would root it there for the connection's lifetime.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.NatsActivitySource || source.Name == "test-ambient",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var ambientSource = new ActivitySource("test-ambient");
        using var ambient = ambientSource.StartActivity("ambient");
        ambient.Should().NotBeNull();
        Activity.Current.Should().BeSameAs(ambient);

        await using var nats = new NatsConnection();

        using var activity = Telemetry.StartReceiveActivity(
            nats,
            name: "receive",
            subscriptionSubject: "foo.bar",
            queueGroup: null,
            subject: "foo.bar",
            replyTo: null,
            bodySize: 0,
            size: 0,
            headers: null);

        activity.Should().NotBeNull();
        activity!.Parent.Should().BeNull();
        Activity.Current.Should().BeSameAs(ambient);
    }

    [Fact]
    public async Task StartReceiveActivity_clears_current_when_the_ambient_activity_has_stopped()
    {
        // The read loop captures Activity.Current when it starts, so it can hold an activity
        // the application later stops. Activity.Current's setter silently refuses to make a
        // finished activity current again, which makes restoring the saved ambient a no-op
        // and leaves the receive activity in Current on the read loop for good.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.NatsActivitySource || source.Name == "test-ambient",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        await using var nats = new NatsConnection();

        using var ambientSource = new ActivitySource("test-ambient");
        using var ambient = ambientSource.StartActivity("ambient");
        ambient.Should().NotBeNull();

        // Capture the execution context while the ambient activity is still running, the way
        // the read loop does, then stop the activity from outside that context.
        var gate = new SemaphoreSlim(0);
        var probe = Task.Run(async () =>
        {
            await gate.WaitAsync().ConfigureAwait(false);

            Activity.Current.Should().BeSameAs(ambient, "the captured context still holds the activity after it stops");

            using var activity = Telemetry.StartReceiveActivity(
                nats,
                name: "receive",
                subscriptionSubject: "foo.bar",
                queueGroup: null,
                subject: "foo.bar",
                replyTo: null,
                bodySize: 0,
                size: 0,
                headers: null);

            activity.Should().NotBeNull();

            return Activity.Current;
        });

        ambient!.Stop();
        gate.Release();

        var current = await probe;

        current.Should().BeNull("a stopped ambient cannot be restored, so Current must be cleared instead of left holding the receive activity");
    }

    [Fact]
    public async Task StartReceiveActivity_restores_the_nearest_running_ancestor_of_a_stopped_ambient()
    {
        // Clearing Current outright is only necessary as far up as the stopped activities go.
        // If the saved ambient stopped but its parent is still running, that parent is the
        // closest thing to the context the caller had.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.NatsActivitySource || source.Name == "test-ambient",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        await using var nats = new NatsConnection();

        using var ambientSource = new ActivitySource("test-ambient");
        using var outer = ambientSource.StartActivity("outer");
        using var inner = ambientSource.StartActivity("inner");
        inner!.Parent.Should().BeSameAs(outer);

        var gate = new SemaphoreSlim(0);
        var probe = Task.Run(async () =>
        {
            await gate.WaitAsync().ConfigureAwait(false);

            using var activity = Telemetry.StartReceiveActivity(
                nats,
                name: "receive",
                subscriptionSubject: "foo.bar",
                queueGroup: null,
                subject: "foo.bar",
                replyTo: null,
                bodySize: 0,
                size: 0,
                headers: null);

            activity.Should().NotBeNull();

            return Activity.Current;
        });

        // Only the inner activity stops; the outer one is still running.
        inner.Stop();
        gate.Release();

        var current = await probe;

        current.Should().BeSameAs(outer);
    }

    [Fact]
    public async Task Reading_a_message_leaves_the_consumer_current_activity_untouched()
    {
        // Activity.Stop() sets Current to the activity's Parent whether or not the activity
        // is current on this context. Receive activities are parented by context and so have
        // no Parent, which means ending one as the consumer reads its message would clear
        // whatever span the application had current.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.NatsActivitySource || source.Name == "test-app",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        await using var nats = new NatsConnection();
        var sub = new NatsSub<string>(nats, new NoopSubscriptionManager(), "foo.bar", queueGroup: null, opts: null, NatsDefaultSerializer<string>.Default);

        // Hand a message off the way the read loop does: no ambient activity current.
        await sub.ReceiveAsync("foo.bar", replyTo: null, headersBuffer: null, payloadBuffer: new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("hi")));

        using var appSource = new ActivitySource("test-app");
        using var app = appSource.StartActivity("app");
        Activity.Current.Should().BeSameAs(app);

        sub.Msgs.TryRead(out var msg).Should().BeTrue();

        msg.Headers?.Activity.Should().NotBeNull();
        msg.Headers!.Activity!.IsStopped.Should().BeTrue("reading the message ends its receive activity");
        Activity.Current.Should().BeSameAs(app);
    }

    [Fact]
    public async Task Dropping_a_message_ends_its_receive_activity()
    {
        // A message evicted from a full subscription channel never reaches a consumer, so the
        // read that would have ended its receive activity never happens.
        var subject = $"drop.{Guid.NewGuid():N}";
        var started = new List<Activity>();

        // The listener is process-global, so only count activities for this test's subject.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.NatsActivitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a =>
            {
                if (a.GetTagItem(Telemetry.Constants.DestName) is string name && name == subject)
                {
                    lock (started)
                        started.Add(a);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await using var nats = new NatsConnection();
        var opts = new NatsSubOpts { ChannelOpts = new NatsSubChannelOpts { Capacity = 1, FullMode = BoundedChannelFullMode.DropOldest } };
        var sub = new NatsSub<string>(nats, new NoopSubscriptionManager(), subject, queueGroup: null, opts, NatsDefaultSerializer<string>.Default);

        await sub.ReceiveAsync(subject, replyTo: null, headersBuffer: null, payloadBuffer: new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("first")));
        await sub.ReceiveAsync(subject, replyTo: null, headersBuffer: null, payloadBuffer: new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("second")));

        // Only the second message is still in the channel; the first one was evicted.
        sub.Msgs.TryRead(out var kept).Should().BeTrue();
        kept.Data.Should().Be("second");
        sub.Msgs.TryRead(out _).Should().BeFalse();

        started.Should().HaveCount(2);
        started.Should().AllSatisfy(a => a.IsStopped.Should().BeTrue(
            "the kept message is ended by the read and the dropped one when it is evicted"));
    }

    [Fact]
    public async Task Receive_failure_is_recorded_on_the_receive_activity_of_the_failing_message()
    {
        // Before receive activities were kept off the ambient context, the failing message's
        // activity happened to be Activity.Current in the handler below. It no longer is, so
        // the exception has to be recorded against the activity the message actually carries,
        // and that activity has to be ended: it never reaches a consumer to be ended there.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.NatsActivitySource || source.Name == "test-app",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        await using var nats = new NatsConnection();
        var sub = new ThrowingSub(nats);

        using var appSource = new ActivitySource("test-app");
        using var app = appSource.StartActivity("app");

        await sub.ReceiveAsync("foo.bar", replyTo: null, headersBuffer: null, payloadBuffer: new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("hi")));

        var activity = sub.BuiltActivity;
        activity.Should().NotBeNull();
        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.Events.Should().Contain(e => e.Name == "exception");
        activity.IsStopped.Should().BeTrue("a message that never reaches a consumer has nowhere else to be ended");

        app.Should().NotBeNull();
        app!.Status.Should().Be(ActivityStatusCode.Unset, "the application's span did not fail");
        Activity.Current.Should().BeSameAs(app);
    }

    [Fact]
    public async Task SpanDestinationName_uses_configured_formatter()
    {
        var previous = NatsInstrumentationOptions.Default.SpanDestinationNameFormatter;
        try
        {
            NatsInstrumentationOptions.Default.SpanDestinationNameFormatter = subject => $"custom:{subject}";

            await using var nats = new NatsConnection();

            nats.SpanDestinationName("foo.bar.baz").Should().Be("custom:foo.bar.baz");
        }
        finally
        {
            NatsInstrumentationOptions.Default.SpanDestinationNameFormatter = previous;
        }
    }

    [Fact]
    public async Task SpanDestinationName_collapses_inbox_before_configured_formatter()
    {
        var previous = NatsInstrumentationOptions.Default.SpanDestinationNameFormatter;
        try
        {
            NatsInstrumentationOptions.Default.SpanDestinationNameFormatter = subject => $"custom:{subject}";

            await using var nats = new NatsConnection();

            nats.SpanDestinationName("_INBOX.abc.def").Should().Be("inbox");
        }
        finally
        {
            NatsInstrumentationOptions.Default.SpanDestinationNameFormatter = previous;
        }
    }

    private sealed class NoopSubscriptionManager : INatsSubscriptionManager
    {
        public ValueTask RemoveAsync(NatsSubBase sub) => default;
    }

    // Stands in for the receive paths that can fail after building the message, such as the
    // JetStream subscriptions parsing consumer metadata off the reply subject.
    private sealed class ThrowingSub : NatsSubBase
    {
        public ThrowingSub(INatsConnection connection)
            : base(connection, new NoopSubscriptionManager(), "foo.bar", queueGroup: null, opts: null)
        {
        }

        public Activity? BuiltActivity { get; private set; }

        protected override ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
        {
            var msg = NatsMsg<string>.Build(subject, replyTo, headersBuffer, payloadBuffer, Connection, Connection.HeaderParser, NatsDefaultSerializer<string>.Default);
            BuiltActivity = msg.Headers?.Activity;
            ReceiveActivity = BuiltActivity;

            // Not a SystemException: those are deliberately left to propagate out of ReceiveAsync.
            throw new NatsException("receive failed");
        }

        protected override void TryComplete()
        {
        }
    }
}
