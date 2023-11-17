namespace NATS.Client.JetStream.Tests;

public class OptionsTests
{
    [Theory]
    [InlineData(null, null, "$JS.API")]
    [InlineData("OTHER", null, "OTHER")]
    [InlineData(null, "ABC", "$JS.API.ABC")]
    [InlineData("$JS.API", null, "$JS.API")]
    [InlineData("$JS.API", "ABC", "$JS.API.ABC")]
    [InlineData("OTHER", "ABC", "OTHER.ABC")]
    public void Api_prefix(string? prefix, string? domain, string expected) =>
        new NatsJSOpts(new NatsOpts(), apiPrefix: prefix, domain: domain).Prefix.Should().Be(expected);
}
