using System.Net.Sockets;
using System.Security.Authentication;
using NATS.Client.Core;
using NATS.Client.TestUtilities2;

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

        // The TLS handshake failure surfaces differently across runtimes and races
        // (AuthenticationException, SocketException, or IOException on net481 when
        // the server resets the connection). Walk the exception chain and accept
        // any of these as evidence that the TLS upgrade was rejected.
        var causes = Unwrap(exception).ToList();
        foreach (var cause in causes)
            _output.WriteLine($"[{cause.GetType().Name}] {cause.Message}");

        Assert.Contains(causes, c => c is AuthenticationException or SocketException or IOException);
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
        await nats.ConnectRetryAsync();

        await using var sub = await nats.SubscribeCoreAsync<int>($"{prefix}.foo");
        for (var i = 0; i < 64; i++)
        {
            await nats.PublishAsync($"{prefix}.foo", i);
            Assert.Equal(i, (await sub.Msgs.ReadAsync()).Data);
        }
    }

    private static IEnumerable<Exception> Unwrap(Exception? ex)
    {
        while (ex != null)
        {
            yield return ex;
            ex = ex.InnerException;
        }
    }
}

public class TlsTestsNatsServerFixture() : BaseNatsServerFixture("resources/configs/tls.conf");
