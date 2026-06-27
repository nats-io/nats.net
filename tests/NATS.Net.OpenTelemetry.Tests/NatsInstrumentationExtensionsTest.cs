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
    public void AddNatsClientInstrumentation_with_configure_sets_options()
    {
        var configured = false;
        try
        {
            using var provider = Sdk.CreateTracerProviderBuilder()
                .AddNatsClientInstrumentation(options =>
                {
                    configured = true;
                    options.Filter = _ => true;
                    options.Enrich = (_, _) => { };
                    options.SpanDestinationNameFormatter = subject => subject;
                })
                .Build();

            configured.Should().BeTrue();
            NatsInstrumentationOptions.Default.Filter.Should().NotBeNull();
            NatsInstrumentationOptions.Default.Enrich.Should().NotBeNull();
            NatsInstrumentationOptions.Default.SpanDestinationNameFormatter.Should().NotBeNull();
        }
        finally
        {
            NatsInstrumentationOptions.Default.Filter = null;
            NatsInstrumentationOptions.Default.Enrich = null;
            NatsInstrumentationOptions.Default.SpanDestinationNameFormatter = null;
        }
    }

    [Fact]
    public void SourceName_is_NATS_Net()
    {
        NatsTelemetry.SourceName.Should().Be("NATS.Net");
    }

    [Theory]
    [InlineData("orders.new", true)]
    [InlineData("orders.new.eu", true)]
    [InlineData("orders", false)] // '>' needs at least one trailing token
    [InlineData("payments.new", false)]
    public void FilterSubjects_include_matches_only_listed(string subject, bool expected)
    {
        var options = new NatsInstrumentationOptions().FilterSubjects(include: ["orders.>"]);

        options.Filter!(Context(subject)).Should().Be(expected);
    }

    [Theory]
    [InlineData("_INBOX.abc.def", false)]
    [InlineData("foo.bar", true)]
    public void FilterSubjects_exclude_drops_listed(string subject, bool expected)
    {
        var options = new NatsInstrumentationOptions().FilterSubjects(exclude: ["_INBOX.>"]);

        options.Filter!(Context(subject)).Should().Be(expected);
    }

    [Theory]
    [InlineData("foo.bar", true)]
    [InlineData("foo.bar.baz", false)] // '*' matches a single token only
    [InlineData("foo", false)]
    public void FilterSubjects_single_token_wildcard(string subject, bool expected)
    {
        var options = new NatsInstrumentationOptions().FilterSubjects(include: ["foo.*"]);

        options.Filter!(Context(subject)).Should().Be(expected);
    }

    [Fact]
    public void FilterSubjects_exclude_wins_over_include()
    {
        var options = new NatsInstrumentationOptions().FilterSubjects(include: ["orders.>"], exclude: ["orders.internal.>"]);

        options.Filter!(Context("orders.new")).Should().BeTrue();
        options.Filter!(Context("orders.internal.audit")).Should().BeFalse();
    }

    [Fact]
    public void FilterSubjects_composes_with_existing_filter()
    {
        var options = new NatsInstrumentationOptions { Filter = ctx => ctx.Subject.StartsWith("orders.", StringComparison.Ordinal) };
        options.FilterSubjects(exclude: ["orders.internal.>"]);

        options.Filter!(Context("orders.new")).Should().BeTrue();
        options.Filter!(Context("orders.internal.audit")).Should().BeFalse(); // dropped by subject exclude
        options.Filter!(Context("payments.new")).Should().BeFalse(); // dropped by the pre-existing filter
    }

    [Fact]
    public void FilterSubjects_without_patterns_leaves_filter_unset()
    {
        var options = new NatsInstrumentationOptions().FilterSubjects();

        options.Filter.Should().BeNull();
    }

    private static NatsInstrumentationContext Context(string subject) =>
        new(subject, Headers: null, ReplyTo: null, QueueGroup: null, BodySize: null, Size: null, Connection: null, ParentContext: default);
}
