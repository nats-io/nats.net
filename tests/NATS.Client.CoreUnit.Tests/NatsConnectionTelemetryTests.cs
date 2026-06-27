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
}
