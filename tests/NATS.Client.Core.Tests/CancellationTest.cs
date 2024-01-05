namespace NATS.Client.Core.Tests;

public class CancellationTest
{
    private readonly ITestOutputHelper _output;

    public CancellationTest(ITestOutputHelper output) => _output = output;

    // should check
    // timeout via command-timeout(request-timeout)
    // timeout via connection dispose
    // cancel manually
    [Fact]
    public async Task CommandTimeoutTest()
    {
        var server = NatsServer.Start(_output, TransportType.Tcp);

        await using var conn = server.CreateClientConnection(NatsOpts.Default with { CommandTimeout = TimeSpan.FromMilliseconds(1) });
        await conn.ConnectAsync();

        // kill the server
        await server.DisposeAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        // wait for reconnect loop to kick in
        while (conn.ConnectionState != NatsConnectionState.Reconnecting)
        {
            await Task.Delay(1, cancellationToken);
        }

        // commands time out
        await Assert.ThrowsAsync<TimeoutException>(() => conn.PingAsync(cancellationToken).AsTask());
        await Assert.ThrowsAsync<TimeoutException>(() => conn.PublishAsync("test", cancellationToken: cancellationToken).AsTask());
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var unused in conn.SubscribeAsync<string>("test", cancellationToken: cancellationToken))
            {
            }
        });
    }

    // Queue-full

    // External Cancellation
}
