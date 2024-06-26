using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NATS.Client.Core;
using Xunit.Abstractions;

namespace NATS.Client.Platform.Windows.Tests;

public class TlsTests
{
    private readonly ITestOutputHelper _output;

    public TlsTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Tls_fails_without_certificates()
    {
        await using var server = await NatsServerProcess.StartAsync(config: "resources/configs/tls.conf");
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var exception = await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());
        Assert.Contains("TLS authentication failed", exception.InnerException?.Message);
        Assert.IsType<AuthenticationException>(exception.InnerException?.InnerException);
    }

    [Fact]
    public async Task Tls_with_certificates()
    {
        const string caCertPem = "resources/certs/ca-cert.pem";
        const string clientCertPem = "resources/certs/client-cert.pem";
        const string clientKeyPem = "resources/certs/client-key.pem";

        var tlsOpts = new NatsTlsOpts
        {
            ConfigureClientAuthentication = o =>
            {
                var clientCert = X509Certificate2Helpers.CreateFromPem(File.ReadAllText(clientCertPem), File.ReadAllText(clientKeyPem));
                o.LoadClientCertFromX509(clientCert);

                var caCerts = new X509Certificate2Collection();
                caCerts.ImportFromPem(File.ReadAllText(caCertPem));
                o.LoadCaCertsFromX509(caCerts);

                return default;
            },
        };

        await using var server = await NatsServerProcess.StartAsync(config: "resources/configs/tls.conf");
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, TlsOpts = tlsOpts });

        await nats.PingAsync();

        await using var sub = await nats.SubscribeCoreAsync<int>("foo");
        for (var i = 0; i < 64; i++)
        {
            await nats.PublishAsync("foo", i);
            Assert.Equal(i, (await sub.Msgs.ReadAsync()).Data);
        }
    }
}
