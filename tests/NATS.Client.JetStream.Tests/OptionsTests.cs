namespace NATS.Client.JetStream.Tests;

public class OptionsTests
{
    [Fact]
    public void Api_prefix()
    {
        new NatsJSOpts(new NatsOpts(), apiPrefix: "$JS.API").Prefix.Should().Be("$JS.API");
        new NatsJSOpts(new NatsOpts(), apiPrefix: "$JS.API", domain: "ABC").Prefix.Should().Be("$JS.API.ABC");
        new NatsJSOpts(new NatsOpts(), apiPrefix: "$JS.API", domain: null).Prefix.Should().Be("$JS.API");
        new NatsJSOpts(new NatsOpts(), apiPrefix: null, domain: null).Prefix.Should().Be("$JS.API");
        new NatsJSOpts(new NatsOpts()).Prefix.Should().Be("$JS.API");
    }
}
