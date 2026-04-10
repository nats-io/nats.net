using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

public class PingCancellationTest
{
    private readonly ITestOutputHelper _output;

    public PingCancellationTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task PingAsync_succeeds_with_mock_server()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = new MockServer(
            handler: (_, _) => Task.CompletedTask,
            logger: m => _output.WriteLine(m),
            cancellationToken: cts.Token);

        await server.Ready;
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();

        var rtt = await nats.PingAsync(cts.Token);
        rtt.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task PingAsync_multiple_sequential_pings_succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = new MockServer(
            handler: (_, _) => Task.CompletedTask,
            logger: m => _output.WriteLine(m),
            cancellationToken: cts.Token);

        await server.Ready;
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();

        for (var i = 0; i < 5; i++)
        {
            var rtt = await nats.PingAsync(cts.Token);
            rtt.Should().BeGreaterThan(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task PingAsync_throws_when_cancelled_waiting_for_pong()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pingCount = 0;

        // autoPong: false — handler replies PONG only for the first PING (the connect handshake)
        await using var server = new MockServer(
            handler: async (client, cmd) =>
            {
                if (cmd.Name == "PING")
                {
                    var n = Interlocked.Increment(ref pingCount);
                    client.Log($"[S] PING #{n}");

                    if (n == 1)
                    {
                        // Reply to the connect-time PING so the connection opens
                        await client.Writer.WriteAsync("PONG\r\n");
                        await client.Writer.FlushAsync();
                    }

                    // Subsequent PINGs get no PONG
                }
            },
            logger: m => _output.WriteLine(m),
            autoPong: false,
            cancellationToken: cts.Token);

        await server.Ready;
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();

        // This ping should time out because the server won't reply PONG
        using var pingCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var act = () => nats.PingAsync(pingCts.Token).AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PingAsync_concurrent_pings_all_complete()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = new MockServer(
            handler: (_, _) => Task.CompletedTask,
            logger: m => _output.WriteLine(m),
            cancellationToken: cts.Token);

        await server.Ready;
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();

        // Fire multiple pings concurrently — exercises the pool and concurrent SetResult paths
        var tasks = new Task<TimeSpan>[10];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = nats.PingAsync(cts.Token).AsTask();
        }

        var results = await Task.WhenAll(tasks);
        foreach (var rtt in results)
        {
            rtt.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task PingAsync_succeeds_against_real_server()
    {
        await using var server = await NatsServerProcess.StartAsync();

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();

        var rtt = await nats.PingAsync();
        rtt.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task PingAsync_times_out_after_server_stopped()
    {
        await using var server = await NatsServerProcess.StartAsync();

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();

        // Verify ping works while server is up
        var rtt = await nats.PingAsync();
        rtt.Should().BeGreaterThan(TimeSpan.Zero);
        _output.WriteLine($"Ping RTT before stop: {rtt}");

        // Stop the server — no more PONGs
        await server.StopAsync();
        _output.WriteLine("Server stopped");

        // Ping with a timeout should throw since no PONG will arrive
        using var pingCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var act = () => nats.PingAsync(pingCts.Token).AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
