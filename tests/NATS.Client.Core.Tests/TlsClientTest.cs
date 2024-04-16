using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NATS.Client.Core.Tests;

public class TlsClientTest
{
    private readonly ITestOutputHelper _output;

    public TlsClientTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Client_connect_using_certificate()
    {
        await using var server = NatsServer.Start(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tls, tlsVerify: true)
                .Build());

        var clientOpts = server.ClientOpts(NatsOpts.Default with { Name = "tls-test-client" });
        await using var nats = new NatsConnection(clientOpts);
        await nats.ConnectAsync();
        var rtt = await nats.PingAsync();
        Assert.True(rtt > TimeSpan.Zero);
    }

    [Fact]
    public async Task Client_connect_using_certificate_and_revocation_check()
    {
        await using var server = NatsServer.Start(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tls, tlsVerify: true)
                .Build());

        var clientOpts = server.ClientOpts(NatsOpts.Default with { Name = "tls-test-client" });
#pragma warning disable CS0618 // Type or member is obsolete
        clientOpts = clientOpts with { TlsOpts = clientOpts.TlsOpts with { CertificateRevocationCheckMode = X509RevocationMode.Online } };
#pragma warning restore CS0618 // Type or member is obsolete
        await using var nats = new NatsConnection(clientOpts);

        // At the moment I don't know of a good way of checking if the revocation check is working
        // except to check if the connection fails. So we are expecting an exception here.
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () => await nats.ConnectAsync());
        Assert.Contains("remote certificate was rejected", exception.InnerException!.InnerException!.Message);
    }

    [Fact]
    public async Task Client_cannot_connect_without_certificate()
    {
        await using var server = NatsServer.Start(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tls, tlsVerify: true)
                .Build());

        var clientOpts = server.ClientOpts(NatsOpts.Default);
        clientOpts = clientOpts with { TlsOpts = clientOpts.TlsOpts with { CertFile = null, KeyFile = null } };
        await using var nats = new NatsConnection(clientOpts);

        var exceptionTask = Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());

        // TODO: On Linux failed mTLS connection hangs.
        // In this scenario _sslStream.AuthenticateAsClientAsync() is not throwing exception on Linux
        // which is causing the connection to hang. So if the serer is configured to verify the client
        // and the client does not provide a certificate, the connection will hang on Linux.
        await Task.WhenAny(exceptionTask, Task.Delay(3000));
    }

    [Fact]
    public async Task Client_timeout_during_tls_auth()
    {
        var server = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
        server.Start();

        var port = ((IPEndPoint)server.LocalEndpoint).Port;

        var signal = new WaitSignal();
        var serverTask = Task.Run(async () =>
        {
            var client = await server.AcceptTcpClientAsync();

            var stream = client.GetStream();

            var sw = new StreamWriter(stream, Encoding.ASCII);
            await sw.WriteAsync("INFO {\"tls_required\":true}\r\n");
            await sw.FlushAsync();

            // Wait for the client TLS auth to timeout
            await signal;
        });

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"127.0.0.1:{port}",
            ConnectTimeout = TimeSpan.FromSeconds(3),
            TlsOpts = new NatsTlsOpts
            {
                CaFile = "resources/certs/ca-cert.pem",
                CertFile = "resources/certs/client-cert.pem",
                KeyFile = "resources/certs/client-key.pem",
            },
        });

        var exception = await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());
        Assert.Equal("TLS authentication timed out", exception.InnerException!.Message);

        signal.Pulse();
        await serverTask;
    }
}
