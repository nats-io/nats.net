#if NET8_0_OR_GREATER
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core2.Tests;

/// <summary>
/// When TLS is required, a transport the client can't upgrade to TLS (a custom
/// <see cref="INatsSocketConnectionFactory"/>, a wrapper returned by
/// <see cref="NatsConnection.OnSocketAvailableAsync"/>, a ws:// URL) must fail the attempt, not
/// carry the connection in plaintext. A transport that does TLS itself says so with
/// <see cref="INatsTlsSocketConnection"/>.
/// </summary>
public class CustomTransportTlsTest(ITestOutputHelper output)
{
    private const string TlsRequiredInfo = "{\"server_id\":\"fake\",\"max_payload\":1048576,\"tls_required\":true}";

    [Fact]
    public async Task Custom_socket_factory_without_tls_fails_closed()
    {
        using var certs = new TestCertificates();
        await using var server = new FakeNatsServer(output) { Info = _ => TlsRequiredInfo };
        var logs = new InMemoryTestLoggerFactory(LogLevel.Error);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://127.0.0.1:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            SocketConnectionFactory = new PlainSocketFactory(),
            LoggerFactory = logs,
        });

        var error = await ConnectErrorAsync(nats);

        server.HasReceived("CONNECT").Should().BeFalse("TLS is required and this transport can't do it");
        error.Should().BeOfType<NatsException>();
        logs.Logs.Should().Contain(m => m.Exception != null && m.Exception.Message.Contains("INatsTlsSocketConnection"));
    }

    [Fact]
    public async Task Socket_wrapped_in_OnSocketAvailableAsync_without_tls_fails_closed()
    {
        using var certs = new TestCertificates();
        await using var server = new FakeNatsServer(output) { Info = _ => TlsRequiredInfo };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://127.0.0.1:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
        });

        // a wrapper that hides the TCP socket, so the client can't upgrade it to TLS
        nats.OnSocketAvailableAsync = socket => new ValueTask<INatsSocketConnection>(new WrappedSocket(socket));

        var error = await ConnectErrorAsync(nats);

        server.HasReceived("CONNECT").Should().BeFalse("TLS is required and this transport can't do it");
        error.Should().BeOfType<NatsException>();
    }

    [Fact]
    public async Task Plain_websocket_url_with_tls_options_fails_closed()
    {
        using var certs = new TestCertificates();
        await using var server = new FakeNatsServer(output);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"ws://127.0.0.1:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
        });

        var error = await ConnectErrorAsync(nats);

        server.Connections.Should().Be(0, "a ws:// URL can't give the TLS the options ask for, so it isn't dialled");
        error.Should().BeOfType<NatsException>();
        error!.ToString().Should().Contain("wss://");
    }

    [Fact]
    public async Task Socket_that_provides_tls_itself_is_used_as_it_is()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);

        // TLS from the first byte: a client that tried to upgrade this connection would fail
        await using var server = new FakeNatsServer(output, FakeTransport.TlsFirst, cert)
        {
            Info = _ => TlsRequiredInfo,
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://127.0.0.1:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            SocketConnectionFactory = new TlsSocketFactory(cert),
        });

        await nats.ConnectAsync();
        await nats.PingAsync();

        server.HasReceived("CONNECT").Should().BeTrue();
    }

    // A socket can do TLS itself and still expose its Socket: it must not get a second handshake.
    [Theory]
    [InlineData(TlsMode.Require)]
    [InlineData(TlsMode.Implicit)]
    public async Task Socket_that_provides_tls_itself_and_is_upgradeable_is_not_upgraded_again(TlsMode mode)
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);

        await using var server = new FakeNatsServer(output, FakeTransport.TlsFirst, cert)
        {
            Info = _ => TlsRequiredInfo,
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://127.0.0.1:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile, Mode = mode },
            SocketConnectionFactory = new TlsSocketFactory(cert, exposeSocket: true),
        });

        await nats.ConnectAsync();
        await nats.PingAsync();

        server.HasReceived("CONNECT").Should().BeTrue();
    }

    [Theory]
    [InlineData(TlsMode.Auto)]
    [InlineData(TlsMode.Disable)]
    public async Task Custom_socket_factory_without_tls_fails_closed_for_a_wss_url(TlsMode mode)
    {
        await using var server = new FakeNatsServer(output);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"wss://127.0.0.1:{server.Port}",
            TlsOpts = new NatsTlsOpts { Mode = mode },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            SocketConnectionFactory = new PlainSocketFactory(),
        });

        var error = await ConnectErrorAsync(nats);

        server.HasReceived("CONNECT").Should().BeFalse("a wss:// URL asks for TLS and this transport can't do it");
        error.Should().BeOfType<NatsException>();
    }

    [Fact]
    public async Task Socket_replaced_in_OnSocketAvailableAsync_after_the_tls_handshake_fails_closed()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);
        await using var server = new FakeNatsServer(output, FakeTransport.TlsFirst, cert) { Info = _ => TlsRequiredInfo };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile, Mode = TlsMode.Implicit },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
        });

        // the client did the handshake on the socket it handed over, not on what comes back
        nats.OnSocketAvailableAsync = socket => new ValueTask<INatsSocketConnection>(new WrappedSocket(socket));

        var error = await ConnectErrorAsync(nats);

        server.HasReceived("CONNECT").Should().BeFalse("the socket was replaced after the TLS handshake");
        error.Should().BeOfType<NatsException>();
    }

    private async Task<Exception?> ConnectErrorAsync(NatsConnection nats)
    {
        try
        {
            await nats.ConnectAsync();
            return null;
        }
        catch (Exception e)
        {
            output.WriteLine($"ConnectAsync: {e}");
            return e;
        }
    }

    // Plain TCP that doesn't expose its socket, like a custom transport (a tunnel, a proxy) would.
    private sealed class PlainSocketFactory : INatsSocketConnectionFactory
    {
        public async ValueTask<INatsSocketConnection> ConnectAsync(Uri uri, NatsOpts opts, CancellationToken cancellationToken)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(uri.Host, uri.Port, cancellationToken);
            return new StreamSocket(tcp, tcp.GetStream());
        }
    }

    private sealed class StreamSocket(TcpClient tcp, Stream stream) : INatsSocketConnection
    {
        public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
        {
            await stream.WriteAsync(buffer);
            return buffer.Length;
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => stream.ReadAsync(buffer);

        public async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync();
            tcp.Dispose();
        }
    }

    private sealed class WrappedSocket(INatsSocketConnection inner) : INatsSocketConnection
    {
        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer) => inner.SendAsync(buffer);

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => inner.ReceiveAsync(buffer);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    // TLS set up by the factory, pinned to the test server's certificate.
    private sealed class TlsSocketFactory(X509Certificate2 serverCert, bool exposeSocket = false) : INatsSocketConnectionFactory
    {
        public async ValueTask<INatsSocketConnection> ConnectAsync(Uri uri, NatsOpts opts, CancellationToken cancellationToken)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(uri.Host, uri.Port, cancellationToken);
            var ssl = new SslStream(tcp.GetStream(), false, (_, certificate, _, _) => certificate?.GetCertHashString() == serverCert.GetCertHashString());
            await ssl.AuthenticateAsClientAsync("localhost");
            return exposeSocket ? new UpgradeableTlsStreamSocket(tcp, ssl) : new TlsStreamSocket(tcp, ssl);
        }
    }

    private sealed class TlsStreamSocket(TcpClient tcp, Stream stream) : INatsTlsSocketConnection
    {
        public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
        {
            await stream.WriteAsync(buffer);
            return buffer.Length;
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => stream.ReadAsync(buffer);

        public async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync();
            tcp.Dispose();
        }
    }

    private sealed class UpgradeableTlsStreamSocket(TcpClient tcp, Stream stream) : INatsTlsSocketConnection, INatsTlsUpgradeableSocketConnection
    {
        public Socket Socket => tcp.Client;

        public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
        {
            await stream.WriteAsync(buffer);
            return buffer.Length;
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => stream.ReadAsync(buffer);

        public async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync();
            tcp.Dispose();
        }
    }
}
#endif
