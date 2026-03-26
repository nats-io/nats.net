using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NATS.Client.Core.Tests;

/// <summary>
/// Verifies TlsMode.Prefer (and TlsMode.Auto over nats://) behavior:
/// TLS is opportunistic, so when the server's INFO does not advertise TLS
/// the connection stays plaintext. This is expected -- use tls:// or
/// TlsMode.Require when TLS is required.
/// </summary>
public class TlsPreferTest(ITestOutputHelper output)
{
    /// <summary>
    /// When INFO has no TLS flags the client proceeds in plaintext,
    /// including any configured credentials in the CONNECT message.
    /// </summary>
    [Fact]
    public async Task Prefer_mode_sends_credentials_in_plaintext_when_info_has_no_tls_flags()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Start a fake server that sends INFO with no TLS flags (MITM scenario)
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var connectLine = string.Empty;

        var serverTask = Task.Run(
            async () =>
            {
#if NET6_0_OR_GREATER
                using var tcp = await listener.AcceptTcpClientAsync(cts.Token);
#else
                using var tcp = await listener.AcceptTcpClientAsync();
#endif
                var stream = tcp.GetStream();

                // ISO 8859-1 (Latin-1): 1-byte encoding where (int)char == byte, for lossless byte round-tripping
                var encoding = Encoding.GetEncoding(28591);
                var sw = new StreamWriter(stream, encoding);
                var sr = new StreamReader(stream, encoding);

                // INFO with no TLS flags (server does not offer TLS)
                await sw.WriteAsync("INFO {\"server_id\":\"fake\",\"max_payload\":1048576,\"tls_required\":false,\"tls_available\":false}\r\n");
                await sw.FlushAsync();

                // Read lines until we see CONNECT
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await sr.ReadLineAsync();
                    if (line == null)
                        break;

                    output.WriteLine($"[SERVER] RCV: {line}");

                    if (line.StartsWith("CONNECT"))
                    {
                        connectLine = line;
                    }
                    else if (line.StartsWith("PING"))
                    {
                        await sw.WriteAsync("PONG\r\n");
                        await sw.FlushAsync();
                        break; // handshake complete
                    }
                }
            },
            cts.Token);

        // Client with credentials using Prefer mode
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{port}",
            AuthOpts = new NatsAuthOpts
            {
                Username = "secret_user",
                Password = "secret_pass",
            },
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Prefer },
        });

        await nats.ConnectAsync();
        await serverTask;
        listener.Stop();

        // CONNECT was sent over plaintext and includes credentials
        connectLine.Should().NotBeEmpty("client should have sent CONNECT");
        connectLine.Should().Contain("secret_user", "credentials were sent over plaintext");
        connectLine.Should().Contain("secret_pass", "credentials were sent over plaintext");

        output.WriteLine($"CONNECT sent in plaintext: {connectLine}");
    }

    /// <summary>
    /// Verifies that TlsMode.Auto resolves to Prefer for nats:// without certs.
    /// </summary>
    [Fact]
    public void Auto_mode_resolves_to_prefer_for_nats_scheme_without_certs()
    {
        var opts = new NatsTlsOpts { Mode = TlsMode.Auto };
        var uri = new Uri("nats://127.0.0.1:4222");

        var effective = opts.EffectiveMode(uri);

        effective.Should().Be(TlsMode.Prefer, "Auto with nats:// and no certs should resolve to Prefer");
    }

    /// <summary>
    /// Verifies that TlsMode.Require refuses to connect when the server
    /// does not advertise TLS -- the secure alternative to Prefer.
    /// </summary>
    [Fact]
    public async Task Require_mode_throws_when_server_does_not_support_tls()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(
            async () =>
            {
#if NET6_0_OR_GREATER
                using var tcp = await listener.AcceptTcpClientAsync(cts.Token);
#else
                using var tcp = await listener.AcceptTcpClientAsync();
#endif
                var stream = tcp.GetStream();

                // ISO 8859-1 (Latin-1): 1-byte encoding where (int)char == byte, for lossless byte round-tripping
                var encoding = Encoding.GetEncoding(28591);
                var sw = new StreamWriter(stream, encoding);

                // Server with no TLS support
                await sw.WriteAsync("INFO {\"server_id\":\"notls\",\"max_payload\":1048576,\"tls_required\":false,\"tls_available\":false}\r\n");
                await sw.FlushAsync();

                // Keep connection open until cancelled
                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            },
            cts.Token);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{port}",
            AuthOpts = new NatsAuthOpts
            {
                Username = "secret_user",
                Password = "secret_pass",
            },
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Require },
        });

        var act = () => nats.ConnectAsync().AsTask();

        await act.Should().ThrowAsync<NatsException>();

        cts.Cancel();
        listener.Stop();
    }

    /// <summary>
    /// Verifies that tls:// scheme resolves to Require, not Prefer.
    /// </summary>
    [Fact]
    public void Tls_scheme_resolves_to_require()
    {
        var opts = new NatsTlsOpts { Mode = TlsMode.Auto };
        var uri = new Uri("tls://127.0.0.1:4222");

        var effective = opts.EffectiveMode(uri);

        effective.Should().Be(TlsMode.Require, "tls:// scheme should always resolve to Require");
    }
}
