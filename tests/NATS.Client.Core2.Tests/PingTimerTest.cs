using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.Core.Tests;

public class PingTimerTest
{
    // The server stops reading but keeps the socket open, like a peer that went away
    // without a FIN or RST. A steady publisher fills the send buffer so PINGs can't be
    // written. The ping timer should still abort the connection after MaxPingOut.
    [Fact]
    public async Task Ping_timer_aborts_connection_when_send_buffer_is_full()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stalled = 0;
        await using var server = new MockServer(
            handler: async (_, cmd) =>
            {
                // stop reading on the first connection only
                if (cmd is { Name: "(PRE)PUB", Subject: "stall" } && Interlocked.Exchange(ref stalled, 1) == 0)
                    await Task.Delay(Timeout.Infinite, cts.Token);
            },
            cancellationToken: cts.Token);

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            PingInterval = TimeSpan.FromSeconds(1),
            MaxPingOut = 2,
            CommandTimeout = TimeSpan.FromSeconds(1),
            RequestReplyMode = NatsRequestReplyMode.SharedInbox,
        });

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        nats.ConnectionDisconnected += (_, _) =>
        {
            disconnected.TrySetResult();
            return default;
        };

        await server.Ready;
        await nats.ConnectRetryAsync();
        await nats.PublishAsync("stall", cancellationToken: cts.Token);

        var publishTimeouts = 0;
        using var pubCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
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

        var aborted = await Task.WhenAny(disconnected.Task, Task.Delay(TimeSpan.FromSeconds(10), cts.Token)) == disconnected.Task;
        pubCts.Cancel();
        await publisher;

        // if the writer never got stuck this test would pass without exercising anything
        Assert.True(publishTimeouts > 0, "send buffer never filled");
        Assert.True(aborted, "connection was not aborted");

        // reconnects to the same server, which reads normally now
        await nats.PingAsync(cts.Token);
    }
}
