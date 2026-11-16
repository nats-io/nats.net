#if NET8_0_OR_GREATER
using NATS.Client.Core.Tests;

namespace NATS.Client.Core2.Tests;

/// <summary>
/// A failed handshake-first (<see cref="TlsMode.Implicit"/>) TLS handshake must end the attempt:
/// the client must not go on in plaintext on that socket, and must close it.
/// </summary>
public class TlsFirstFailureTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Initial_connect_does_not_fall_back_to_plaintext_after_rejecting_the_certificate()
    {
        using var certs = new TestCertificates();
        using var untrusted = certs.CreateUntrustedServerCert(["localhost"]);
        await using var server = new FakeNatsServer(output, FakeTransport.TlsFirstThenPlaintext, untrusted);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{server.Port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Implicit, CaFile = certs.CaFile },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
        });

        Exception? error = null;
        try
        {
            await nats.ConnectAsync();
        }
        catch (Exception e)
        {
            error = e;
            output.WriteLine($"ConnectAsync: {e}");
        }

        server.HasReceived("CONNECT").Should().BeFalse("CONNECT must not be sent after the TLS handshake failed");
        error.Should().BeOfType<NatsException>();
        nats.ConnectionState.Should().Be(NatsConnectionState.Closed);

        // the socket of the failed attempt is closed, not left for the GC
        await server.WaitForClosedAsync(1, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Initial_connect_fails_when_the_next_seed_is_down()
    {
        using var certs = new TestCertificates();
        using var untrusted = certs.CreateUntrustedServerCert(["localhost"]);
        await using var server = new FakeNatsServer(output, FakeTransport.TlsFirstThenPlaintext, untrusted);
        var dead = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        dead.Start();
        var deadPort = ((System.Net.IPEndPoint)dead.LocalEndpoint).Port;
        dead.Stop();

        // the failed seed is tried first, then the one that is down
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{server.Port},tls://127.0.0.1:{deadPort}",
            NoRandomize = true,
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Implicit, CaFile = certs.CaFile },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
        });

        Exception? error = null;
        try
        {
            await nats.ConnectAsync();
        }
        catch (Exception e)
        {
            error = e;
            output.WriteLine($"ConnectAsync: {e}");
        }

        server.HasReceived("CONNECT").Should().BeFalse("CONNECT must not be sent after the TLS handshake failed");
        error.Should().BeOfType<NatsException>("no seed was reachable over TLS");
        nats.ConnectionState.Should().Be(NatsConnectionState.Closed);
    }

    [Fact]
    public async Task Reconnect_closes_the_socket_of_a_failed_handshake()
    {
        using var certs = new TestCertificates();
        using var trusted = certs.CreateServerCert(["localhost"]);
        using var untrusted = certs.CreateUntrustedServerCert(["localhost"]);
        await using var bad = new FakeNatsServer(output, FakeTransport.TlsFirstThenPlaintext, untrusted, "bad");
        await using var seed = new FakeNatsServer(output, FakeTransport.TlsFirst, trusted, "seed")
        {
            Info = _ => $"{{\"server_id\":\"seed\",\"max_payload\":1048576,\"connect_urls\":[\"127.0.0.1:{bad.Port}\"]}}",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{seed.Port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Implicit, CaFile = certs.CaFile },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });

        await nats.ConnectAsync();
        await seed.StopAsync();

        await Retry.Until("the client tries the discovered server", () => bad.Connections > 0);
        await bad.WaitForClosedAsync(1, TimeSpan.FromSeconds(5));
        bad.HasReceived("CONNECT").Should().BeFalse("CONNECT must not be sent after the TLS handshake failed");
    }

    [Fact]
    public async Task Dispose_ends_a_handshake_that_the_server_never_completes()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeNatsServer(output, FakeTransport.TlsFirst, cert)
        {
            BeforeTlsHandshake = _ => hold.Task,
        };

        var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{server.Port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Implicit, CaFile = certs.CaFile },
            ConnectTimeout = TimeSpan.FromSeconds(60),
        });

        try
        {
            var connect = nats.ConnectAsync().AsTask();
            await Retry.Until("the server holds the handshake", () => server.Connections == 1);
            await Task.Delay(200);

            var dispose = nats.DisposeAsync().AsTask();
            var first = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(10)));
            first.Should().BeSameAs(connect, "disposing closes the socket of the handshake in progress");
            await Assert.ThrowsAnyAsync<Exception>(() => connect);
            await dispose;
        }
        finally
        {
            hold.TrySetResult();
        }
    }
}
#endif
