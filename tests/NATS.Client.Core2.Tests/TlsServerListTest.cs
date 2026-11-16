#if NET8_0_OR_GREATER
using NATS.Client.Core.Tests;

namespace NATS.Client.Core2.Tests;

/// <summary>
/// A tls:// or wss:// URL among the configured servers asks for TLS for every server.
/// <see cref="TlsMode.Prefer"/> is the opt-out, but not for such a URL itself.
/// </summary>
public class TlsServerListTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Tls_url_makes_the_whole_server_list_tls()
    {
        // neither server offers TLS; the nats:// URL is the one that gets dialled
        await using var first = new FakeNatsServer(output, name: "first");
        await using var second = new FakeNatsServer(output, name: "second");

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{first.Port},tls://127.0.0.1:{second.Port}",
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            NoRandomize = true,
        });

        var error = await ConnectErrorAsync(nats);

        error.Should().BeOfType<NatsException>();
        first.Connections.Should().Be(1);
        first.HasReceived("CONNECT").Should().BeFalse("the tls:// URL asks for TLS for the nats:// URL too");

        // the mode is Auto: the error says what made TLS required
        error!.ToString().Should().Contain("a tls:// or wss:// URL is among the configured servers");
        error.ToString().Should().NotContain("TlsMode is set to Require");
    }

    [Fact]
    public async Task Wss_url_makes_the_whole_server_list_tls()
    {
        await using var second = new FakeNatsServer(output, name: "second");

        // nothing listens on the wss:// port
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"wss://127.0.0.1:1,nats://127.0.0.1:{second.Port}",
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            NoRandomize = true,
        });

        var error = await ConnectErrorAsync(nats);

        error.Should().BeOfType<NatsException>();
        second.Connections.Should().Be(1);
        second.HasReceived("CONNECT").Should().BeFalse("the wss:// URL asks for TLS for the nats:// URL too");
    }

    [Fact]
    public async Task Prefer_keeps_each_url_as_configured()
    {
        await using var first = new FakeNatsServer(output, name: "first");
        await using var second = new FakeNatsServer(output, name: "second");

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{first.Port},tls://127.0.0.1:{second.Port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Prefer },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            NoRandomize = true,
        });

        await nats.ConnectAsync();
        await nats.PingAsync();

        first.HasReceived("CONNECT").Should().BeTrue("with Prefer the tls:// URL does not ask for TLS for the nats:// URL");
    }

    [Fact]
    public async Task Prefer_does_not_take_tls_from_a_tls_url()
    {
        await using var server = new FakeNatsServer(output);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://127.0.0.1:{server.Port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Prefer },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
        });

        var error = await ConnectErrorAsync(nats);

        error.Should().BeOfType<NatsException>();
        server.HasReceived("CONNECT").Should().BeFalse("a tls:// URL is never dialled without TLS");
    }

    [Fact]
    public async Task Plain_websocket_url_next_to_a_tls_url_is_refused()
    {
        await using var first = new FakeNatsServer(output, name: "first");
        await using var second = new FakeNatsServer(output, name: "second");

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://127.0.0.1:{first.Port},ws://127.0.0.1:{second.Port}",
            NoRandomize = true,
        });

        var error = await ConnectErrorAsync(nats);

        error.Should().BeOfType<NatsException>();
        error!.ToString().Should().Contain("wss://");
        error.ToString().Should().Contain("a tls:// or wss:// URL is among the configured servers");
        first.Connections.Should().Be(0, "the list is refused before any attempt");
        second.Connections.Should().Be(0);

        // the refusal leaves the connection closed: connecting again fails again, it does not wait
        nats.ConnectionState.Should().Be(NatsConnectionState.Closed);
        var again = ConnectErrorAsync(nats);
        (await Task.WhenAny(again, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(again);
        (await again).Should().BeOfType<NatsException>();
    }

    // A server learned over TLS keeps TLS when a plaintext connection reports it again, also
    // when the entry is spelled differently.
    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    public async Task Server_learned_over_tls_keeps_tls_when_reported_in_plaintext(string host)
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);

        // offers no TLS: a plaintext CONNECT here means the TLS it was learned with is gone
        await using var peer = new FakeNatsServer(output, name: "peer");
        await using var tlsSeed = new FakeNatsServer(output, FakeTransport.StartTls, cert, "tls-seed")
        {
            Info = _ => "{\"server_id\":\"tls-seed\",\"max_payload\":1048576,\"tls_required\":true}",
            AfterPong = (_, pong) => pong == 1 ? $"INFO {{\"server_id\":\"tls-seed\",\"max_payload\":1048576,\"connect_urls\":[\"localhost:{peer.Port}\"]}}\r\n" : null,
        };
        await using var plainSeed = new FakeNatsServer(output, name: "plain-seed")
        {
            Info = _ => $"{{\"server_id\":\"plain-seed\",\"max_payload\":1048576,\"connect_urls\":[\"{host}:{peer.Port}\"]}}",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{tlsSeed.Port},nats://127.0.0.1:{plainSeed.Port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Prefer, CaFile = certs.CaFile },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            NoRandomize = true,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            LoggerFactory = FakeNatsServer.ClientLogger(output),
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();
        await nats.PingAsync();

        await tlsSeed.StopAsync();
        await Retry.Until("on the plaintext seed", () => plainSeed.HasReceived("CONNECT") && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(20));
        await nats.PingAsync();
        var dialled = peer.Connections;

        plainSeed.DisconnectAll();
        await Retry.Until("the peer is dialled again", () => peer.Connections > dialled, timeout: TimeSpan.FromSeconds(20));
        await Retry.Until("back on the plaintext seed", () => plainSeed.Connections >= 2 && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(20));

        peer.HasReceived("CONNECT").Should().BeFalse("the peer was learned over TLS and offers none");
    }

    [Theory]
    [InlineData("tls")]
    [InlineData("wss")]
    public async Task Disable_with_tls_url_fails_before_dialling(string scheme)
    {
        await using var server = new FakeNatsServer(output);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"{scheme}://127.0.0.1:{server.Port}",
            TlsOpts = new NatsTlsOpts { Mode = TlsMode.Disable },
        });

        var error = await ConnectErrorAsync(nats);

        error.Should().BeOfType<NatsException>();
        error!.Message.Should().Contain("requires TLS but TlsMode is set to Disable");
        server.Connections.Should().Be(0);
    }

    // A configuration that can never connect is not retried: with RetryOnInitialConnect it
    // would otherwise retry forever without an error or a log line.
    [Theory]
    [InlineData("tls://127.0.0.1:{0}", TlsMode.Disable)]
    [InlineData("wss://127.0.0.1:{0}", TlsMode.Disable)]
    [InlineData("tls://127.0.0.1:{0},ws://127.0.0.1:{0}", TlsMode.Auto)]
    public async Task Tls_configuration_error_is_not_retried(string url, TlsMode mode)
    {
        await using var server = new FakeNatsServer(output);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = string.Format(url, server.Port),
            TlsOpts = new NatsTlsOpts { Mode = mode },
            RetryOnInitialConnect = true,
        });

        var connect = ConnectErrorAsync(nats);
        (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(connect, "a configuration error is not retried");
        (await connect).Should().BeOfType<NatsException>();
        server.Connections.Should().Be(0);
        nats.ConnectionState.Should().Be(NatsConnectionState.Closed);
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
}
#endif
