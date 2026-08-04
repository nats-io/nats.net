using System.Diagnostics;
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
}
