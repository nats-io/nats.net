#if NET8_0_OR_GREATER
namespace NATS.Client.Core2.Tests;

/// <summary>
/// Plaintext bytes that arrive with or after INFO, before the TLS upgrade, must be discarded:
/// they can't have come from the server the client then authenticates.
/// </summary>
public class StartTlsInjectionTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Plaintext_after_info_is_not_processed_after_the_upgrade()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);
        await using var server = new FakeNatsServer(output, FakeTransport.StartTls, cert)
        {
            Info = _ => "{\"server_id\":\"fake\",\"max_payload\":1048576,\"tls_required\":true}",
            AfterInfo = _ => "PING\r\nPING\r\nINFO {\"server_id\":\"injected\",\"max_payload\":1048576,\"connect_urls\":[\"sentinel-injected.invalid:4222\"]}\r\n",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
        });
        await nats.ConnectAsync();
        await nats.PingAsync();

        server.HasReceived("PONG").Should().BeFalse("a PING injected before the TLS upgrade must not be answered");
        nats.ServerInfo!.Id.Should().Be("fake");
        nats.ServerInfo.ClientConnectUrls.Should().BeNull();
    }

    [Fact]
    public async Task Plaintext_during_the_handshake_fails_the_attempt()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);
        await using var server = new FakeNatsServer(output, FakeTransport.StartTls, cert)
        {
            Info = _ => "{\"server_id\":\"fake\",\"max_payload\":1048576,\"tls_required\":true}",
        };

        // the client has read INFO and started its handshake by the time these bytes arrive
        server.BeforeTlsHandshake = async _ =>
        {
            await Task.Delay(300);
            await server.SendAsync("PING\r\n-ERR 'Authorization Violation'\r\n");
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
        });

        var connect = async () => await nats.ConnectAsync();
        await connect.Should().ThrowAsync<NatsException>();

        server.HasReceived("PONG").Should().BeFalse("plaintext in place of the handshake is not protocol");
        server.HasReceived("CONNECT").Should().BeFalse();
    }
}
#endif
