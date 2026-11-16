#if NET8_0_OR_GREATER
using NATS.Client.Core.Tests;

namespace NATS.Client.Core2.Tests;

/// <summary>
/// The server always sends INFO first. Anything that arrives before it was not sent by the
/// server the client is about to authenticate (an on-path attacker can prepend it to the
/// plaintext INFO), so it must not be acted on.
/// </summary>
public class InfoFirstTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Op_before_info_fails_the_connection_attempt()
    {
        using var certs = new TestCertificates();
        using var cert = certs.CreateServerCert(["localhost"]);
        await using var server = new FakeNatsServer(output, FakeTransport.StartTls, cert)
        {
            BeforeInfo = _ => "PING\r\n",
            Info = _ => "{\"server_id\":\"fake\",\"max_payload\":1048576,\"tls_required\":true}",
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"tls://localhost:{server.Port}",
            TlsOpts = new NatsTlsOpts { CaFile = certs.CaFile },
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

        // answering the injected PING over TLS would mean acting on unauthenticated plaintext
        await Task.Delay(500);
        server.HasReceived("PONG").Should().BeFalse("the PING before INFO must not be answered");
        error.Should().BeOfType<NatsException>();
        error!.ToString().Should().Contain("INFO");
    }

    [Fact]
    public async Task Msg_before_info_on_reconnect_is_not_delivered()
    {
        string? sid = null;
        await using var server = new FakeNatsServer(output)
        {
            BeforeInfo = n => n == 2 && sid != null ? $"MSG foo {sid} 8\r\ninjected\r\n" : null,
        };

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{server.Port}",
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
        });
        await nats.ConnectAsync();

        await using var sub = await nats.SubscribeCoreAsync<string>("foo");
        await Retry.Until("SUB arrives", () => server.HasReceived("SUB foo "));
        sid = server.Received.First(r => r.Line.StartsWith("SUB foo ", StringComparison.Ordinal)).Line.Split(' ')[2];

        server.DisconnectAll();

        // connection 2 starts with the forged MSG; the client must not take it, and must get
        // back to a working connection
        await Retry.Until("reconnected", () => server.Connections >= 2 && nats.ConnectionState == NatsConnectionState.Open);
        await nats.PingAsync();

        sub.Msgs.TryRead(out var msg).Should().BeFalse($"a MSG sent before INFO must not be delivered, got '{msg.Data}'");
    }
}
#endif
