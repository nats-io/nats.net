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

        await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());
    }
}
