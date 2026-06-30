namespace NATS.Client.CoreUnit.Tests;

public class NatsConnectionTelemetryTests
{
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
