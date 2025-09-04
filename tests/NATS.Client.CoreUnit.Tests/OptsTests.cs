using Microsoft.Extensions.Configuration;

public class OptsUrlTests
{
    [Fact]
    public void Default_opts_should_be_equivalent_but_not_same_as_new()
    {
        // Arrange
        var opts1 = NatsOpts.Default;
        var opts2 = new NatsOpts();
        var opts3 = NatsOpts.Default;

        // Assert
        opts1.Should().BeEquivalentTo(opts2);
        opts1.Should().NotBeSameAs(opts2);
        opts1.AuthOpts.Should().NotBeSameAs(opts2.AuthOpts);
        opts1.TlsOpts.Should().NotBeSameAs(opts2.TlsOpts);
        opts1.WebSocketOpts.Should().NotBeSameAs(opts2.WebSocketOpts);
        opts1.SerializerRegistry.Should().BeSameAs(opts2.SerializerRegistry);
        opts1.LoggerFactory.Should().BeSameAs(opts2.LoggerFactory);

        opts1.Should().BeEquivalentTo(opts3);
        opts1.Should().BeSameAs(opts3);
        opts1.AuthOpts.Should().BeSameAs(opts3.AuthOpts);
        opts1.TlsOpts.Should().BeSameAs(opts3.TlsOpts);
        opts1.WebSocketOpts.Should().BeSameAs(opts3.WebSocketOpts);
        opts1.SerializerRegistry.Should().BeSameAs(opts3.SerializerRegistry);
        opts1.LoggerFactory.Should().BeSameAs(opts3.LoggerFactory);
    }

    [Fact]
    public void Should_bind_configuration_to_natsopts_only_once()
    {
        // Arrange
        var configValues = new Dictionary<string, string?>
        {
            { "SOURCE:AUTHOPTS:USERNAME", "HELLO" },
            { "SOURCE:TLSOPTS:CERTBUNDLEFILEPASSWORD", "HELLO" },
            { "SOURCE:WEBSOCKETOPTS:REQUESTHEADERS:HELLO", null },
            { "TARGET:AUTHOPTS:USERNAME", "WORLD" },
            { "TARGET:TLSOPTS:CERTBUNDLEFILEPASSWORD", "WORLD" },
            { "TARGET:WEBSOCKETOPTS:REQUESTHEADERS:WORLD", null },
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        // Act
        var opts1 = new NatsOpts();
        configuration.Bind("SOURCE", opts1);
        var opts2 = new NatsOpts();
        configuration.Bind("TARGET", opts2);

        // Assert
        opts1.AuthOpts.Username.Should().Be("HELLO");
        opts1.TlsOpts.CertBundleFilePassword.Should().Be("HELLO");
        opts1.WebSocketOpts.RequestHeaders.Should().ContainKey("HELLO");

        opts2.AuthOpts.Username.Should().Be("WORLD");
        opts2.TlsOpts.CertBundleFilePassword.Should().Be("WORLD");
        opts2.WebSocketOpts.RequestHeaders.Should().ContainKey("WORLD");
    }
}
