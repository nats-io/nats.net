using System.Collections.Concurrent;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core2.Tests;

public class DisposeUnobservedExceptionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Dispose_after_connect_does_not_cause_UnobservedTaskException()
    {
        var unobserved = new ConcurrentBag<Exception>();

        void Handler(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception is null)
                return;

            foreach (var inner in args.Exception.InnerExceptions)
            {
                if ((inner.StackTrace ?? string.Empty).Contains("NATS.Client.Core"))
                    unobserved.Add(inner);
            }
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            await using (var server = await NatsServerProcess.StartAsync())
            {
                var nats = new NatsConnection(new NatsOpts { Url = server.Url });
                await nats.ConnectRetryAsync();
                await using var sub = await nats.SubscribeCoreAsync<int>("regression.test");
                await nats.DisposeAsync();
            }

            await Task.Delay(200);
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }

        unobserved.Should().BeEmpty(because: "dispose should observe all background tasks");
    }

    [Fact]
    public async Task Dispose_during_reconnect_does_not_cause_UnobservedTaskException()
    {
        // Reproduce the orphan-reconnect-task path: complete the initial handshake,
        // disconnect to push the client into the reconnect loop, then refuse PONG so
        // the PONG wait inside SetupReaderWriterAsync(true) times out. Without the
        // fix, the reconnectTask spawned beforehand is abandoned and faults later.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pingCount = 0;

        await using var server = new MockServer(
            handler: async (client, cmd) =>
            {
                if (cmd.Name == "PING")
                {
                    var n = Interlocked.Increment(ref pingCount);
                    if (n == 1)
                    {
                        // initial handshake: reply PONG so the connection opens.
                        // Don't drop the socket here: ConnectAsync must observe the
                        // Open state first, otherwise it re-waits for a reconnect
                        // that never succeeds and the test hangs.
                        await client.Writer.WriteAsync("PONG\r\n");
                        await client.Writer.FlushAsync();
                    }

                    // subsequent PINGs: stay silent so PONG wait times out
                }
                else if (cmd is { Name: "PUB", Subject: "drop.connection" })
                {
                    // deterministic disconnect once the test is fully set up
                    client.Close();
                }
            },
            logger: m => output.WriteLine(m),
            autoPong: false,
            cancellationToken: cts.Token);

        await server.Ready;

        var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            ConnectTimeout = TimeSpan.FromMilliseconds(500),
            ReconnectWaitMin = TimeSpan.FromMilliseconds(50),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(50),
        });

        var unobserved = new ConcurrentBag<Exception>();

        void Handler(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception is null)
                return;

            foreach (var inner in args.Exception.InnerExceptions)
            {
                if ((inner.StackTrace ?? string.Empty).Contains("NATS.Client.Core"))
                    unobserved.Add(inner);
            }
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            await nats.ConnectAsync();

            // Subscribe so WriteReconnectCommandsAsync has work to do on the reconnect
            // attempt that gets orphaned.
            _ = await nats.SubscribeCoreAsync<int>("regression.test");

            // Tell the mock server to drop the socket, pushing the client into the
            // reconnect loop where every PONG wait times out.
            await nats.PublishAsync("drop.connection");

            // Wait long enough for at least one reconnect attempt to time out.
            await Task.Delay(1500);

            await nats.DisposeAsync();

            await Task.Delay(200);
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }

        unobserved.Should().BeEmpty(because: "the orphaned reconnect task must be observed");
    }
}
