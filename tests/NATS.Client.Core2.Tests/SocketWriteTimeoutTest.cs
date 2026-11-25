using System.Globalization;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.Core.Tests;

public class SocketWriteTimeoutTest
{
    private readonly ITestOutputHelper _output;

    public SocketWriteTimeoutTest(ITestOutputHelper output) => _output = output;

    // The server stops reading on the first connection but keeps the socket open, like a peer
    // that went away without a FIN or RST. The ping interval is long, so only the socket write
    // timeout can abort the connection here. The command timeout is well below the socket write
    // timeout, so publishes time out on the full send buffer before the abort.
    [Fact]
    public async Task Socket_write_timeout_aborts_connection_when_peer_stops_reading()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = CreateServerThatStallsFirstConnection(cts.Token);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            PingInterval = TimeSpan.FromMinutes(5),
            CommandTimeout = TimeSpan.FromMilliseconds(250),
            SocketWriteTimeout = TimeSpan.FromSeconds(2),
        });

        var disconnected = WaitForDisconnect(nats);
        await server.Ready;
        await nats.ConnectRetryAsync();
        await nats.PublishAsync("stall", cancellationToken: cts.Token);

        var (aborted, publishTimeouts) = await PublishUntilAsync(nats, disconnected, TimeSpan.FromSeconds(10), cts.Token);

        Assert.True(publishTimeouts > 0, "send buffer never filled");
        Assert.True(aborted, "connection was not aborted");

        // reconnects to the same server, which reads normally now
        await nats.PingAsync(cts.Token);
    }

    [Fact]
    public async Task Socket_write_timeout_disabled_keeps_stalled_connection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = CreateServerThatStallsFirstConnection(cts.Token);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            PingInterval = TimeSpan.FromMinutes(5),
            CommandTimeout = TimeSpan.FromSeconds(1),
            SocketWriteTimeout = TimeSpan.Zero,

            // no inbox subscription, so disposing the still stalled connection has no UNSUB to wait for
            RequestReplyMode = NatsRequestReplyMode.SharedInbox,
        });

        var disconnected = WaitForDisconnect(nats);
        await server.Ready;
        await nats.ConnectRetryAsync();
        await nats.PublishAsync("stall", cancellationToken: cts.Token);

        var (aborted, publishTimeouts) = await PublishUntilAsync(nats, disconnected, TimeSpan.FromSeconds(5), cts.Token);

        Assert.True(publishTimeouts > 0, "send buffer never filled");
        Assert.False(aborted, "connection was aborted although the socket write timeout is disabled");
    }

    // Beyond the largest System.Threading.Timer period (about 49.7 days)
    [Theory]
    [InlineData("100.00:00:00")]
    [InlineData("10675199.02:48:05.4775807")] // TimeSpan.MaxValue
    public async Task Socket_write_timeout_beyond_timer_range_connects(string socketWriteTimeout)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = new MockServer(
            handler: (_, _) => Task.CompletedTask,
            logger: m => _output.WriteLine(m),
            cancellationToken: cts.Token);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            SocketWriteTimeout = TimeSpan.Parse(socketWriteTimeout, CultureInfo.InvariantCulture),
        });

        await server.Ready;
        await nats.ConnectAsync();
        await nats.PingAsync(cts.Token);
    }

    private static Task WaitForDisconnect(NatsConnection nats)
    {
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        nats.ConnectionDisconnected += (_, _) =>
        {
            disconnected.TrySetResult();
            return default;
        };
        return disconnected.Task;
    }

    private static async Task<(bool Aborted, int PublishTimeouts)> PublishUntilAsync(NatsConnection nats, Task disconnected, TimeSpan wait, CancellationToken cancellationToken)
    {
        var publishTimeouts = 0;
        using var pubCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var publisher = Task.Run(async () =>
        {
            var payload = new byte[64 * 1024];
            while (!pubCts.IsCancellationRequested)
            {
                try
                {
                    await nats.PublishAsync("data", payload, cancellationToken: pubCts.Token);
                }
                catch (NatsTimeoutException)
                {
                    Interlocked.Increment(ref publishTimeouts);
                }
                catch (OperationCanceledException)
                {
                }
                catch (NatsException)
                {
                }
            }
        });

        var aborted = await Task.WhenAny(disconnected, Task.Delay(wait, cancellationToken)) == disconnected;
        pubCts.Cancel();
        await publisher;
        return (aborted, Volatile.Read(ref publishTimeouts));
    }

    private MockServer CreateServerThatStallsFirstConnection(CancellationToken cancellationToken)
    {
        var stalled = 0;
        return new MockServer(
            handler: async (_, cmd) =>
            {
                if (cmd is { Name: "(PRE)PUB", Subject: "stall" } && Interlocked.Exchange(ref stalled, 1) == 0)
                    await Task.Delay(Timeout.Infinite, cancellationToken);
            },
            logger: m => _output.WriteLine(m),
            cancellationToken: cancellationToken);
    }
}
