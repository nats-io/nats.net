namespace NATS.Client.Core.Tests;

public class CancellationTest
{
    private readonly ITestOutputHelper _output;

    public CancellationTest(ITestOutputHelper output) => _output = output;

    // check CommandTimeout
    [Fact]
    public async Task CommandTimeoutTest()
    {
        await using var server = NatsServer.Start();

        await using var conn = server.CreateClientConnection(NatsOpts.Default with { CommandTimeout = TimeSpan.FromMilliseconds(1) });
        await conn.ConnectAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        // stall the flush task
        var stopToken = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stallTask = conn.CommandWriter.TestStallFlushAsync(TimeSpan.FromSeconds(10), stopToken.Token);

        // commands that call ConnectAsync throw OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(() => conn.PingAsync(cancellationToken).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => conn.PublishAsync("test", cancellationToken: cancellationToken).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in conn.SubscribeAsync<string>("test", cancellationToken: cancellationToken))
            {
            }
        });

        stopToken.Cancel();
        await stallTask;
    }

    // check that cancellation works on commands that call ConnectAsync
    [Fact]
    public async Task CommandConnectCancellationTest()
    {
        var server = NatsServer.Start(_output, TransportType.Tcp);

        await using var conn = server.CreateClientConnection();
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

        // cancel cts
        cts.Cancel();

        // commands that call ConnectAsync throw TaskCanceledException
        await Assert.ThrowsAsync<TaskCanceledException>(() => conn.PingAsync(cancellationToken).AsTask());
        await Assert.ThrowsAsync<TaskCanceledException>(() => conn.PublishAsync("test", cancellationToken: cancellationToken).AsTask());

        // Because of a race condition minimization / workaround, the following test will throw an OperationCanceledException
        // rather than a TaskCanceledException.
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in conn.SubscribeAsync<string>("test", cancellationToken: cancellationToken))
            {
            }
        });

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            // Give NatsSubBase class a good chance to complete its constructors
            var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await foreach (var unused in conn.SubscribeAsync<string>("test", cancellationToken: cts2.Token))
            {
            }
        });
    }
}
