using System.Diagnostics;
using System.Net.Sockets;
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
        Assert.True(exception.InnerException?.InnerException is SocketException or AuthenticationException);
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

        Exception? exception = null;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                await nats.ConnectAsync();
                exception = null;
                break;
            }
            catch (Exception ex)
            {
                exception = ex;
                await Task.Delay(1000);
            }
        }

        if (exception is not null)
        {
            throw exception;
        }

        await using var sub = await nats.SubscribeCoreAsync<int>($"{prefix}.foo");
        for (var i = 0; i < 64; i++)
        {
            await nats.PublishAsync($"{prefix}.foo", i);
            Assert.Equal(i, (await sub.Msgs.ReadAsync()).Data);
        }
    }
}

public class TlsTestsNatsServerFixture() : BaseNatsServerFixture("resources/configs/tls.conf");
