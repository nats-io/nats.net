namespace NATS.Client.JetStream.Tests;

public class OptionsTests
{
    [Theory]
    [InlineData(null, null, "$JS.API")]
    [InlineData("OTHER", null, "OTHER")]
    [InlineData(null, "ABC", "$JS.ABC.API")]
    [InlineData("$JS.API", null, "$JS.API")]
    [InlineData("any", "any", "exception")]
    public void Api_prefix(string? prefix, string? domain, string expected)
    {
        if (expected == "exception")
        {
            Assert.Throws<NatsJSException>(() => new NatsJSOpts(new NatsOpts(), apiPrefix: prefix, domain: domain));
            return;
        }

        new NatsJSOpts(new NatsOpts(), apiPrefix: prefix, domain: domain).Prefix.Should().Be(expected);
    }
}
