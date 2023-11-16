namespace NATS.Client.Core.Tests;

public class ConnectionRetryTest
{
    private readonly ITestOutputHelper _output;

    public ConnectionRetryTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Max_retry_reached_after_disconnect()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection(new NatsOpts
        {
            MaxReconnectRetry = 2,
            ReconnectWaitMax = TimeSpan.Zero,
            ReconnectWaitMin = TimeSpan.FromSeconds(.1),
        });

        var signal = new WaitSignal();
        nats.ReconnectFailed += (_, e) => signal.Pulse();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await server.StopAsync();

        await signal;
        var exception = await Assert.ThrowsAsync<NatsException>(async () => await nats.PingAsync(cts.Token));
        Assert.Equal("Max connect retry exceeded.", exception.Message);
    }

    [Fact]
    public async Task Retry_and_connect_after_disconnected()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection(new NatsOpts
        {
            MaxReconnectRetry = 10,
            ReconnectWaitMax = TimeSpan.Zero,
            ReconnectWaitMin = TimeSpan.FromSeconds(2),
        });

        var signal = new WaitSignal();
        nats.ReconnectFailed += (_, e) => signal.Pulse();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await server.StopAsync();

        await signal;

        await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

        server.StartServerProcess();

        var rtt = await nats.PingAsync(cts.Token);
        Assert.True(rtt > TimeSpan.Zero);
    }
}
