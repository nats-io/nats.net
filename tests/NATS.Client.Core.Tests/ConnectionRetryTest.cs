namespace NATS.Client.Core.Tests;

public class ConnectionRetryTest
{
    private readonly ITestOutputHelper _output;

    public ConnectionRetryTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Max_retry_reached_after_disconnect()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection(new NatsOpts { MaxReconnectRetry = 2, ReconnectWaitMax = TimeSpan.Zero, ReconnectWaitMin = TimeSpan.FromSeconds(.1), });

        var signal = new WaitSignal();
        nats.ReconnectFailed += (_, _) =>
        {
            signal.Pulse();
            return default;
        };

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
        await using var nats = server.CreateClientConnection(new NatsOpts { MaxReconnectRetry = 10, ReconnectWaitMax = TimeSpan.Zero, ReconnectWaitMin = TimeSpan.FromSeconds(2), });

        var signal = new WaitSignal();
        nats.ReconnectFailed += (_, _) =>
        {
            signal.Pulse();
            return default;
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await server.StopAsync();

        await signal;

        await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

        server.StartServerProcess();

        var rtt = await nats.PingAsync(cts.Token);
        Assert.True(rtt > TimeSpan.Zero);
    }

    [Fact]
    public async Task Reconnect_doesnt_drop_partially_sent_msgs()
    {
        const int msgSize = 1048576; // 1MiB
        await using var server = NatsServer.Start();

        await using var pubConn = server.CreateClientConnection();
        await pubConn.ConnectAsync();

        var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var received = 0;
        var subActive = 0;
        var subTask = Task.Run(async () =>
        {
            await using var subConn = server.CreateClientConnection();
            await using var sub = await subConn.SubscribeCoreAsync<NatsMemoryOwner<byte>>("test", cancellationToken: timeoutCts.Token);
            await foreach (var msg in sub.Msgs.ReadAllAsync(timeoutCts.Token))
            {
                using (msg.Data)
                {
                    if (msg.Data.Length == 1)
                    {
                        Interlocked.Increment(ref subActive);
                    }
                    else if (msg.Data.Length == 2)
                    {
                        break;
                    }
                    else
                    {
                        Assert.Equal(msgSize, msg.Data.Length);
                        Interlocked.Increment(ref received);
                    }
                }
            }
        });

        while (Interlocked.CompareExchange(ref subActive, 0, 0) == 0)
        {
            await pubConn.PublishAsync("test", new byte[1], cancellationToken: timeoutCts.Token);
            await Task.Delay(50, timeoutCts.Token);
        }

        var sent = 0;
        var data = new byte[msgSize];
        var sendTask = Task.Run(async () =>
        {
            while (!stopCts.IsCancellationRequested)
            {
                await pubConn.PublishAsync("test", data, cancellationToken: timeoutCts.Token);
                Interlocked.Increment(ref sent);
            }

            await pubConn.PublishAsync("test", new byte[2], cancellationToken: timeoutCts.Token);
        });

        var reconnects = 0;
        var restartTask = Task.Run(async () =>
        {
            while (!stopCts.IsCancellationRequested)
            {
                if (pubConn is { ConnectionState: NatsConnectionState.Open, TestSocket.WaitForClosed.IsCanceled: false })
                {
                    await pubConn.TestSocket.AbortConnectionAsync(timeoutCts.Token);
                    Interlocked.Increment(ref reconnects);
                }

                // give it some time to send more
                await Task.Delay(100, timeoutCts.Token);
            }
        });

        await Task.WhenAll(subTask, sendTask, restartTask);
        Assert.True(reconnects > 0, "connection did not reconnect");
        Assert.True(received <= sent, $"duplicate messages sent on wire- {sent} sent, {received} received");

        _output.WriteLine($"reconnects: {reconnects}, sent: {sent}, received: {received}");

        // some messages may still be lost, as socket could have been disconnected
        // after socket.WriteAsync returned, but before OS sent
        // check to ensure that the loss was < 1%
        var loss = 100.0 - (100.0 * received / sent);
        Assert.True(loss <= 1.0, $"message loss of {loss:F}% was above 1% - {sent} sent, {received} received");
    }
}
