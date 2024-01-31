using System.Diagnostics;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core.Tests;

public class SendBufferTest
{
    private readonly ITestOutputHelper _output;

    public SendBufferTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Send_cancel()
    {
        void Log(string m) => TmpFileLogger.Log(m);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var server = new MockServer(
            async (s, cmd) =>
            {
                if (cmd.Name == "PUB" && cmd.Subject == "pause")
                {
                    s.Log("[S] pause");
                    await Task.Delay(10_000, cts.Token);
                }
            },
            Log,
            cts.Token);

        Log("__________________________________");

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        Log($"[C] connect {server.Url}");
        await nats.ConnectAsync();

        Log($"[C] ping");
        var rtt = await nats.PingAsync(cts.Token);
        Log($"[C] ping rtt={rtt}");

        server.Log($"[C] publishing pause...");
        await nats.PublishAsync("pause", "x", cancellationToken: cts.Token);

        server.Log($"[C] publishing 1M...");
        var payload = new byte[1024 * 1024];
        var tasks = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            var i1 = i;
            tasks.Add(Task.Run(async () =>
            {
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    Log($"[C] ({i1}) publish...");
                    await nats.PublishAsync("x", payload, cancellationToken: cts.Token);
                }
                catch (Exception e)
                {
                    stopwatch.Stop();
                    Log($"[C] ({i1}) publish cancelled after {stopwatch.Elapsed.TotalSeconds:n0} s (exception: {e.GetType()})");
                    return;
                }

                stopwatch.Stop();
                Log($"[C] ({i1}) publish took {stopwatch.Elapsed.TotalSeconds:n3} s");
            }));
        }

        for (var i = 0; i < 10; i++)
        {
            Log($"[C] await tasks {i}...");
            await tasks[i];
        }
    }
}
