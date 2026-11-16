#if NET8_0_OR_GREATER
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core2.Tests;

/// <summary>
/// How the client uses the servers it learns from INFO connect_urls.
/// </summary>
public class DiscoveredServersTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("ws://{0}")]
    [InlineData("nats://{0}")]
    [InlineData("10.9.8.7:4333@{0}")]
    [InlineData("{0}/path")]
    public async Task Entry_that_is_not_host_and_port_is_never_dialled(string entryFormat)
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);

        // a plaintext server that records what the client does there
        await using var canary = new FakeNatsServer(output, name: "canary");
        var entry = string.Format(entryFormat, $"127.0.0.1:{canary.Port}");
        await using var seed = new FakeNatsServer(output, FakeTransport.StartTls, cert, "seed")
        {
            Info = _ => "{\"server_id\":\"seed\",\"max_payload\":1048576,\"tls_required\":true}",

            // over TLS, as nats-server 2.12+ sends it: connect_urls from the plaintext INFO are not used
            AfterPong = (_, pong) => pong == 1 ? $"INFO {{\"server_id\":\"seed\",\"max_payload\":1048576,\"connect_urls\":[\"{entry}\"]}}\r\n" : null,
        };

        // InsecureSkipVerify stands in for the system trust store: like it, it is not a
        // "certificate option", so TlsMode.Auto resolves per URL scheme.
        var logger = new InMemoryTestLoggerFactory(LogLevel.Warning, m => output.WriteLine($"LOG: {m.Message}"));
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{seed.Port}",
            LoggerFactory = logger,
            TlsOpts = new NatsTlsOpts { InsecureSkipVerify = true },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();
        await nats.PingAsync();
        await seed.StopAsync();

        // the entry is skipped with a log line; a few rounds of the reconnect loop go by
        await Retry.Until("the entry is reported", () => logger.Logs.Any(l => l.Message.StartsWith("Ignoring invalid server URL", StringComparison.Ordinal)), timeout: TimeSpan.FromSeconds(10));
        await Task.Delay(1500);

        canary.Connections.Should().Be(0, "an entry with a scheme, userinfo or path is not a server address");
        nats.ConnectionState.Should().Be(NatsConnectionState.Reconnecting);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_connect_urls_entries_are_skipped(bool asyncInfo)
    {
        await using var peer = new FakeNatsServer(output, name: "peer");
        string?[] connectUrls =
        [
            null,
            string.Empty,
            "::::",
            "[::1",
            "127.0.0.1:notaport",
            "127.0.0.1:99999",
            "a b:4222",
            "%zz:4222",
            "nats://user:pa ss@127.0.0.1:4222",
            "[fe80::1%25en0]:4222x",
            "\u0000:1",
            $"127.0.0.1:{peer.Port}",
        ];
        var info = $"{{\"server_id\":\"seed\",\"max_payload\":1048576,\"connect_urls\":{JsonSerializer.Serialize(connectUrls)}}}";
        await using var seed = new FakeNatsServer(output, name: "seed");
        if (!asyncInfo)
            seed.Info = _ => info;

        var logger = new InMemoryTestLoggerFactory(LogLevel.Warning, m => output.WriteLine($"LOG: {m.Message}"));
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{seed.Port}",
            LoggerFactory = logger,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();
        if (asyncInfo)
        {
            await seed.SendAsync($"INFO {info}\r\n");
            await Retry.Until("async INFO applied", () => nats.ServerInfo?.ClientConnectUrls?.Length == connectUrls.Length);
        }

        await seed.StopAsync();

        await Retry.Until("failed over to the valid peer", () => peer.HasReceived("CONNECT") && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
        await nats.PingAsync();

        // each skipped entry is logged on its own
        logger.Logs.Count(l => l.Message.StartsWith("Ignoring", StringComparison.Ordinal)).Should().BeGreaterThanOrEqualTo(2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Tls_client_does_not_go_plaintext_to_a_discovered_server(bool caFile, bool tlsAvailable)
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);

        // INFO without tls_required, as from a server that allows non-TLS clients, or with the
        // flags stripped on the way
        var flags = tlsAvailable ? ",\"tls_required\":false,\"tls_available\":true" : ",\"tls_required\":false";
        await using var peer = new FakeNatsServer(output, name: "peer")
        {
            Info = _ => $"{{\"server_id\":\"peer\",\"max_payload\":1048576{flags}}}",
        };
        await using var seed = new FakeNatsServer(output, FakeTransport.StartTls, cert, "seed")
        {
            Info = _ => "{\"server_id\":\"seed\",\"max_payload\":1048576,\"tls_required\":true}",

            // over TLS, as nats-server 2.12+ sends it: connect_urls from the plaintext INFO are not used
            AfterPong = (_, pong) => pong == 1 ? $"INFO {{\"server_id\":\"seed\",\"max_payload\":1048576,\"connect_urls\":[\"127.0.0.1:{peer.Port}\"]}}\r\n" : null,
        };

        // without a CA file, InsecureSkipVerify stands in for the system trust store
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{seed.Port}",
            TlsOpts = caFile ? new NatsTlsOpts { CaFile = certs.CaFile } : new NatsTlsOpts { InsecureSkipVerify = true },
            AuthOpts = new NatsAuthOpts { Username = "u", Password = "secret" },
            LoggerFactory = FakeNatsServer.ClientLogger(output),
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();

        // the INFO over TLS follows the first PONG
        await nats.PingAsync();
        await seed.StopAsync();

        await Retry.Until("the discovered peer is tried", () => peer.Connections >= 2, timeout: TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        peer.HasReceived("CONNECT").Should().BeFalse();
    }

    [Fact]
    public async Task Info_from_a_server_that_fails_tls_verification_is_not_used()
    {
        using var certs = new TestCertificates();
        using var trusted = certs.CreateServerCert(["localhost"]);
        using var untrusted = certs.CreateUntrustedServerCert(["localhost"]);
        await using var canary = new FakeNatsServer(output, FakeTransport.StartTls, trusted, "canary")
        {
            Info = _ => "{\"server_id\":\"canary\",\"max_payload\":1048576,\"tls_required\":true}",
        };
        await using var seed = new FakeNatsServer(output, FakeTransport.StartTls, untrusted, "seed")
        {
            Info = _ => $"{{\"server_id\":\"seed\",\"max_payload\":1048576,\"tls_required\":true,\"connect_urls\":[\"localhost:{canary.Port}\",\"127.0.0.1:{canary.Port}\"]}}",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{seed.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            RetryOnInitialConnect = true,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        _ = Task.Run(async () =>
        {
            try
            {
                await nats.ConnectAsync();
            }
            catch
            {
                // the seed never verifies
            }
        });

        await Retry.Until("the seed is tried a few times", () => seed.Connections >= 3, timeout: TimeSpan.FromSeconds(10));
        canary.Connections.Should().Be(0, "connect_urls from a server that failed TLS verification must not be used");
    }
}
#endif
