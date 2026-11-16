#if NET8_0_OR_GREATER
using System.Threading.Channels;
using NATS.Client.Core.Tests;

namespace NATS.Client.Core2.Tests;

/// <summary>
/// Which INFO the list of discovered servers comes from. connect_urls in the plaintext INFO
/// before a TLS upgrade are not authenticated and are not used: on such a connection the
/// servers come from the INFOs received over TLS only.
/// </summary>
public class ServerInfoTrustTest(ITestOutputHelper output)
{
    private const string MaxPayload = "\"max_payload\":1048576";

    [Fact]
    public async Task Pre_tls_connect_urls_are_not_used()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);
        await using var canary = new FakeNatsServer(output, FakeTransport.StartTls, cert, "canary")
        {
            Info = _ => $"{{\"server_id\":\"canary\",{MaxPayload},\"tls_required\":true}}",
        };

        // a server before 2.12: no INFO after CONNECT, so nothing restates the list over TLS
        await using var seed = new FakeNatsServer(output, FakeTransport.StartTls, cert, "seed")
        {
            Info = _ => $"{{\"server_id\":\"seed\",{MaxPayload},\"tls_required\":true,\"connect_urls\":[\"localhost:{canary.Port}\"]}}",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{seed.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();
        await nats.PingAsync();
        seed.DisconnectAll();

        await Retry.Until("reconnected", () => seed.Connections >= 2 && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
        canary.Connections.Should().Be(0, "connect_urls from the plaintext INFO are not used");
    }

    [Fact]
    public async Task Info_over_tls_sets_the_servers()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);
        await using var canary = new FakeNatsServer(output, FakeTransport.StartTls, cert, "canary")
        {
            Info = _ => $"{{\"server_id\":\"canary\",{MaxPayload},\"tls_required\":true}}",
        };
        await using var peer = new FakeNatsServer(output, FakeTransport.StartTls, cert, "peer")
        {
            Info = _ => $"{{\"server_id\":\"peer\",{MaxPayload},\"tls_required\":true}}",
        };

        // 2.12+: INFO after the first PONG, over TLS
        await using var seed = new FakeNatsServer(output, FakeTransport.StartTls, cert, "seed")
        {
            Info = _ => $"{{\"server_id\":\"seed\",{MaxPayload},\"tls_required\":true,\"connect_urls\":[\"localhost:{canary.Port}\"]}}",
            AfterPong = (_, pong) => pong == 1 ? $"INFO {{\"server_id\":\"seed\",{MaxPayload},\"connect_urls\":[\"127.0.0.1:{peer.Port}\"]}}\r\n" : null,
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{seed.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();
        await nats.PingAsync();
        await seed.StopAsync();

        await Retry.Until("reconnected to the peer from the INFO over TLS", () => peer.HasReceived("CONNECT") && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
        canary.Connections.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"connect_urls\":[]")]
    [InlineData(",\"connect_info\":true")]
    public async Task Info_without_connect_urls_keeps_discovered_servers(string fields)
    {
        await using var peer = new FakeNatsServer(output, name: "peer");
        await using var seed = new FakeNatsServer(output, name: "seed")
        {
            Info = _ => $"{{\"server_id\":\"seed\",{MaxPayload},\"connect_urls\":[\"127.0.0.1:{peer.Port}\"]}}",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{seed.Port}",
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();

        // e.g. a lame duck or max_payload update that doesn't list the cluster
        await seed.SendAsync($"INFO {{\"server_id\":\"seed\",{MaxPayload}{fields}}}\r\n");
        await nats.PingAsync();
        await seed.StopAsync();

        await Retry.Until("reconnected to the discovered peer", () => peer.HasReceived("CONNECT") && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task First_info_of_a_new_connection_without_connect_urls_keeps_discovered_servers()
    {
        // the peer drops its first connection, so the client goes back to the seed
        await using var peer = new FakeNatsServer(output, name: "peer")
        {
            CloseAfterPong = (n, _) => n == 1,
        };

        // only the first connection lists the peer
        await using var seed = new FakeNatsServer(output, name: "seed")
        {
            Info = n => n == 1
                ? $"{{\"server_id\":\"seed\",{MaxPayload},\"connect_urls\":[\"127.0.0.1:{peer.Port}\"]}}"
                : $"{{\"server_id\":\"seed\",{MaxPayload}}}",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{seed.Port}",
            NoRandomize = true,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();

        seed.DisconnectAll();
        await Retry.Until("back on the seed", () => seed.Connections >= 2 && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
        await nats.PingAsync();
        await seed.StopAsync();

        await Retry.Until("reconnected to the discovered peer", () => peer.Connections >= 2 && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Pre_tls_connect_urls_do_not_replace_servers_learned_over_tls()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);
        await using var canary = new FakeNatsServer(output, FakeTransport.StartTls, cert, "canary")
        {
            Info = _ => $"{{\"server_id\":\"canary\",{MaxPayload},\"tls_required\":true}}",
        };

        // the peer refuses its first connection, so the client goes back to the seed first
        await using var peer = new FakeNatsServer(output, FakeTransport.StartTls, cert, "peer")
        {
            Info = _ => $"{{\"server_id\":\"peer\",{MaxPayload},\"tls_required\":true}}",
            BeforeTlsHandshake = n => n == 1 ? Task.FromException(new IOException("refused")) : Task.CompletedTask,
        };

        // Connection 1 learns the peer over TLS. On connection 2 the plaintext INFO was rewritten
        // to list another server, and nothing follows over TLS (a server before 2.12).
        await using var seed = new FakeNatsServer(output, FakeTransport.StartTls, cert, "seed")
        {
            Info = n => n == 1
                ? $"{{\"server_id\":\"seed\",{MaxPayload},\"tls_required\":true}}"
                : $"{{\"server_id\":\"seed\",{MaxPayload},\"tls_required\":true,\"connect_urls\":[\"localhost:{canary.Port}\"]}}",
            AfterPong = (n, pong) => n == 1 && pong == 1 ? $"INFO {{\"server_id\":\"seed\",{MaxPayload},\"connect_urls\":[\"127.0.0.1:{peer.Port}\"]}}\r\n" : null,
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{seed.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
            NoRandomize = true,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();
        await nats.PingAsync();

        seed.DisconnectAll();
        await Retry.Until("back on the seed", () => seed.Connections >= 2 && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
        await nats.PingAsync();
        await seed.StopAsync();

        await Retry.Until("failed over to the peer learned over TLS", () => peer.HasReceived("CONNECT") && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
        canary.Connections.Should().Be(0);
    }

    // The reader of a closed connection still drains what it has buffered, and a slow
    // subscriber can hold it there until after the reconnect. An INFO in that buffer must not
    // change the server info or the server list of the next connection.
    [Fact]
    public async Task Info_buffered_on_a_closed_connection_does_not_reach_the_next_one()
    {
        await using var canary = new FakeNatsServer(output, name: "canary");
        await using var seed = new FakeNatsServer(output, name: "seed")
        {
            Info = n => $"{{\"server_id\":\"seed{n}\",{MaxPayload}}}",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{seed.Port}",
            NoRandomize = true,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var subOpts = new NatsSubOpts { ChannelOpts = new NatsSubChannelOpts { Capacity = 1, FullMode = BoundedChannelFullMode.Wait } };
        await using var sub = await nats.SubscribeCoreAsync<string>("foo", opts: subOpts, cancellationToken: cts.Token);
        await nats.PingAsync(cts.Token);
        var sid = seed.Received.First(r => r.Line.StartsWith("SUB foo ", StringComparison.Ordinal)).Line.Split(' ')[^1];

        // the second message holds the reader, with the INFO and the last message behind it
        var msg = $"MSG foo {sid} 1\r\nx\r\n";
        var staleInfo = $"INFO {{\"server_id\":\"stale\",{MaxPayload},\"connect_urls\":[\"127.0.0.1:{canary.Port}\"]}}\r\n";
        await seed.SendAsync($"{msg}{msg}{msg}{staleInfo}{msg}");
        await Retry.Until("first message", () => sub.Msgs.Count == 1, timeout: TimeSpan.FromSeconds(10));
        seed.DisconnectAll();

        // the held reader doesn't see the close: a failed write does
        await Retry.Until(
            "reconnected",
            () => seed.Connections >= 2 && nats.ConnectionState == NatsConnectionState.Open && nats.ServerInfo?.Id == "seed2",
            async () =>
            {
                try
                {
                    await nats.PublishAsync("bar", cancellationToken: cts.Token);
                }
                catch (NatsException)
                {
                }
            },
            timeout: TimeSpan.FromSeconds(10));

        // the last message comes out only after the old reader has gone past the INFO
        for (var i = 0; i < 4; i++)
            await sub.Msgs.ReadAsync(cts.Token);

        nats.ServerInfo!.Id.Should().Be("seed2");

        await nats.PingAsync(cts.Token);
        seed.DisconnectAll();
        await Retry.Until("reconnected again", () => seed.Connections >= 3 && nats.ConnectionState == NatsConnectionState.Open, timeout: TimeSpan.FromSeconds(10));
        canary.Connections.Should().Be(0, "the server list from the closed connection's INFO is not used");
    }

    [Fact]
    public async Task Lame_duck_flag_in_the_first_info_raises_the_event()
    {
        await using var seed = new FakeNatsServer(output, name: "seed")
        {
            Info = _ => $"{{\"server_id\":\"seed\",{MaxPayload},\"ldm\":true}}",
        };

        await using var nats = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{seed.Port}" });
        var events = 0;
        nats.LameDuckModeActivated += (_, _) =>
        {
            Interlocked.Increment(ref events);
            return default;
        };
        await nats.ConnectAsync();

        await Retry.Until("lame duck event", () => Volatile.Read(ref events) == 1, timeout: TimeSpan.FromSeconds(5));
    }
}
#endif
