namespace NATS.Client.Core.Tests;

public class TlsFirstTest
{
    private readonly ITestOutputHelper _output;

    public TlsFirstTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Tls_first_connection()
    {
        if (!NatsServer.SupportsTlsFirst())
        {
            _output.WriteLine($"TLS first is NOT supported by the server");
            return;
        }

        _output.WriteLine($"TLS first is supported by the server");

        await using var server = NatsServer.Start(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tls, tlsFirst: true)
                .Build());

        var clientOpts = server.ClientOpts(NatsOpts.Default);

        Assert.True(clientOpts.TlsOpts.Mode == TlsMode.Implicit);

        // TLS first connection
        {
            await using var nats = new NatsConnection(clientOpts);
            await nats.ConnectAsync();
            var rtt = await nats.PingAsync();
            Assert.True(rtt > TimeSpan.Zero);
            _output.WriteLine($"Implicit TLS connection (RTT: {rtt})");
        }

        // Normal TLS connection should fail
        {
            await using var nats = new NatsConnection(clientOpts with { TlsOpts = clientOpts.TlsOpts with { Mode = TlsMode.Auto } });

            var exception = await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());

            Assert.Matches(@"can not start to connect nats server: tls://", exception.Message);

            _output.WriteLine($"Auto TLS connection rejected");
        }
    }
}
