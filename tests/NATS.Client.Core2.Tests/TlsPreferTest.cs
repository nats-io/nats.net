using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Synadia.Orbit.Testing.GoHarness;
using Synadia.Orbit.Testing.NatsServerProcessManager;

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
            ConnectTimeout = TimeSpan.FromSeconds(10),
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

    /// <summary>
    /// Replicates nats-io/nats-server#7927: server behind a TLS-terminating proxy
    /// has tls: {} + allow_non_tls, so INFO advertises tls_available=true.
    /// Client in Prefer/Auto mode sees tls_available and attempts a TLS upgrade,
    /// which fails because the server (plain TCP behind the proxy) cannot do TLS.
    /// </summary>
    [Fact]
    public async Task Prefer_mode_attempts_tls_upgrade_when_server_advertises_tls_available()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clientAttemptedTls = false;
        var clientSentConnect = false;

        var serverTask = Task.Run(
            async () =>
            {
#if NET6_0_OR_GREATER
                using var tcp = await listener.AcceptTcpClientAsync(cts.Token);
#else
                using var tcp = await listener.AcceptTcpClientAsync();
#endif
                var stream = tcp.GetStream();
                var encoding = Encoding.GetEncoding(28591);
                var sw = new StreamWriter(stream, encoding);

                // Server behind TLS proxy: tls_required=false, tls_available=true
                await sw.WriteAsync("INFO {\"server_id\":\"proxy-backend\",\"max_payload\":1048576,\"tls_required\":false,\"tls_available\":true}\r\n");
                await sw.FlushAsync();

                // Identify the client's next action from its first byte:
                //   0x16 -> TLS Handshake record (ContentType=Handshake), client tried TLS upgrade
                //   0x43 -> 'C' of plaintext "CONNECT ...", client stayed plaintext
                var firstByte = new byte[1];
                int read;
                try
                {
                    read = await stream.ReadAsync(firstByte, 0, 1, cts.Token);
                }
                catch (IOException ex)
                {
                    output.WriteLine($"[SERVER] RCV failed: {ex.Message}");
                    read = 0;
                }

                if (read == 1 && firstByte[0] == 0x16)
                {
                    output.WriteLine("[SERVER] RCV: TLS Handshake record (0x16)");
                    clientAttemptedTls = true;
                }
                else if (read == 1 && firstByte[0] == (byte)'C')
                {
                    clientSentConnect = true;
                    using var sr = new StreamReader(stream, encoding);
                    var rest = await sr.ReadLineAsync();
                    var line = "C" + rest;
                    output.WriteLine($"[SERVER] RCV: {line}");
                    if (!line.StartsWith("CONNECT"))
                    {
                        output.WriteLine("[SERVER] RCV: expected CONNECT but got malformed line");
                    }
                }
                else
                {
                    output.WriteLine($"[SERVER] RCV: unexpected (read={read}, byte=0x{(read == 1 ? firstByte[0] : 0):X2})");
                }
            },
            cts.Token);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Prefer },
            MaxReconnectRetry = 0,
        });

        Exception? connectException = null;
        try
        {
            await nats.ConnectAsync();
        }
        catch (Exception ex)
        {
            // Expected: TLS handshake fails because server can't do TLS
            connectException = ex;
        }

        await serverTask;
        listener.Stop();

        // Guard against the actual bug: Prefer mode skipping TLS upgrade and sending plaintext CONNECT.
        clientSentConnect.Should().BeFalse(
            "Prefer mode must not send plaintext CONNECT when server advertises tls_available=true");

        if (clientAttemptedTls)
        {
            return;
        }

        // On net481 the client can tear the TCP socket down (RST) after a very early
        // TLS init failure, so the ClientHello byte is sometimes never observed by the
        // server. Fall back to ConnectAsync's exception chain as evidence that the
        // client went down the TLS upgrade path.
        var causes = new List<Exception>();
        for (var e = connectException; e != null; e = e.InnerException)
        {
            causes.Add(e);
            output.WriteLine($"[{e.GetType().Name}] {e.Message}");
        }

        var tlsRelated = causes.Any(c => c is AuthenticationException || c is SocketException || c is IOException);
        tlsRelated.Should().BeTrue(
            "Prefer mode should attempt TLS upgrade when server advertises tls_available=true");
    }

    /// <summary>
    /// TlsMode.Disable skips TLS upgrade even when server advertises tls_available=true.
    /// This is the workaround for the TLS-terminating proxy scenario (nats-io/nats-server#7927).
    /// </summary>
    [Fact]
    public async Task Disable_mode_skips_tls_when_server_advertises_tls_available()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

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
                var encoding = Encoding.GetEncoding(28591);
                var sw = new StreamWriter(stream, encoding);
                var sr = new StreamReader(stream, encoding);

                // Server behind TLS proxy: tls_required=false, tls_available=true
                await sw.WriteAsync("INFO {\"server_id\":\"proxy-backend\",\"max_payload\":1048576,\"tls_required\":false,\"tls_available\":true}\r\n");
                await sw.FlushAsync();

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
                        break;
                    }
                }
            },
            cts.Token);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Disable },
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });

        await nats.ConnectAsync();
        await serverTask;
        listener.Stop();

        connectLine.Should().NotBeEmpty("client should have sent CONNECT in plaintext");
        output.WriteLine($"Disable mode connected in plaintext: {connectLine}");
    }

    /// <summary>
    /// Real nats-server with tls: {} + allow_non_tls (nats-io/nats-server#7927 scenario).
    /// .NET client in Prefer mode attempts TLS upgrade and succeeds because the server
    /// can actually do TLS. Disable mode stays plaintext.
    /// </summary>
    [Fact]
    public async Task Real_server_tls_available_prefer_upgrades_disable_stays_plaintext()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var conf = Path.GetFullPath("tls_available_test.conf");
        var confContents = """
            tls {}
            allow_non_tls: true
            """;
        File.WriteAllText(conf, confContents);

        await using var server = await NatsServerProcess.StartAsync(config: conf);
        output.WriteLine($"Server started at {server.Url}");

        // Prefer mode: attempts TLS upgrade, fails because the auto-generated
        // server cert can't complete the handshake. This is the issue scenario.
        await using var natsPrefer = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Prefer },
            MaxReconnectRetry = 0,
        });

        var act = () => natsPrefer.ConnectAsync().AsTask();
        await act.Should().ThrowAsync<Exception>("Prefer mode tries TLS upgrade which fails");
        output.WriteLine("Prefer mode: failed as expected (tried TLS upgrade)");

        // Disable mode: skips TLS, stays plaintext, connects fine
        await using var natsDisable = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Disable },
        });

        await natsDisable.ConnectAsync();
        await natsDisable.PingAsync(cts.Token);
        output.WriteLine("Disable mode: connected in plaintext");
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Go client against real nats-server with tls_available=true.
    /// Go ignores tls_available and connects plaintext without Secure() option.
    /// </summary>
    [Fact]
    public async Task Real_server_tls_available_go_client_stays_plaintext()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        var conf = Path.GetFullPath("tls_available_go_test.conf");
        var confContents = """
            tls {}
            allow_non_tls: true
            """;
        File.WriteAllText(conf, confContents);

        await using var server = await NatsServerProcess.StartAsync(config: conf);
        output.WriteLine($"Server started at {server.Url}");

        // lang=go
        var goCode = $$"""
            package main

            import (
                "fmt"
                "os"

                "github.com/nats-io/nats.go"
            )

            func main() {
                // Plain nats:// without Secure() -- should connect in plaintext
                nc, err := nats.Connect("{{server.Url}}",
                    nats.MaxReconnects(0),
                )
                if err != nil {
                    fmt.Fprintf(os.Stderr, "connect error: %v\n", err)
                    fmt.Println("CONNECT_FAILED")
                    os.Exit(1)
                }

                fmt.Println("CONNECTED_PLAINTEXT")
                nc.Close()
            }
            """;

        await using var go = await GoProcess.RunCodeAsync(
            goCode,
            logger: m => output.WriteLine($"[GO] {m}"),
            goModules: ["github.com/nats-io/nats.go@latest"],
            cancellationToken: cts.Token);

        var line = await go.ReadLineAsync(cts.Token);
        output.WriteLine($"Go output: {line}");

        await go.WaitForExitAsync(cts.Token);
        go.ExitCode.Should().Be(0, "Go client should connect successfully");
        line.Should().Be("CONNECTED_PLAINTEXT", "Go client should connect in plaintext despite tls_available=true");
    }
#endif
}
