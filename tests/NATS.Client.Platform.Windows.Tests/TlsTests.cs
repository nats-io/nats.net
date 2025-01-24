using System.Security.Authentication;
using NATS.Client.Core;

namespace NATS.Client.Platform.Windows.Tests;

public class TlsTests : IClassFixture<TlsTestsNatsServerFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly TlsTestsNatsServerFixture _server;

    public TlsTests(ITestOutputHelper output, TlsTestsNatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Tls_fails_without_certificates()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });

        var exception = await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());
        Assert.Matches("TLS authentication failed|Unable to read data from the transport", exception.InnerException?.Message);
        Assert.IsType<AuthenticationException>(exception.InnerException?.InnerException);
    }

    [Fact]
    public async Task Tls_with_certificates()
    {
        const string caCertFile = "resources/certs/ca-cert.pem";
        const string clientCertBundleFile = "resources/certs/client-cert-bundle.pfx";
        var prefix = _server.GetNextId();

        var tlsOpts = new NatsTlsOpts
        {
            CaFile = caCertFile,
            CertBundleFile = clientCertBundleFile,
        };

        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, TlsOpts = tlsOpts });

        await nats.PingAsync();

        await using var sub = await nats.SubscribeCoreAsync<int>($"{prefix}.foo");
        for (var i = 0; i < 64; i++)
        {
            await nats.PublishAsync($"{prefix}.foo", i);
            Assert.Equal(i, (await sub.Msgs.ReadAsync()).Data);
        }
    }
}

public class TlsTestsNatsServerFixture() : BaseNatsServerFixture("resources/configs/tls.conf");
