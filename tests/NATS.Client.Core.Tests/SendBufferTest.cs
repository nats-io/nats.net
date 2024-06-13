using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core.Tests;

public class SendBufferTest
{
    private readonly ITestOutputHelper _output;

    public SendBufferTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Send_cancel()
    {
        // void Log(string m) => TmpFileLogger.Log(m);
        void Log(string m)
        {
        }

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
            cancellationToken: cts.Token);

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
            tasks.Add(Task.Run(
                async () =>
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
                },
                cts.Token));
        }

        for (var i = 0; i < 10; i++)
        {
            Log($"[C] await tasks {i}...");
            await tasks[i];
        }
    }

    [Fact]
    public async Task Send_recover_half_sent()
    {
        // void Log(string m) => TmpFileLogger.Log(m);
        void Log(string m)
        {
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        List<string> pubs = new();
        await using var server = new MockServer(
            handler: (client, cmd) =>
            {
                if (cmd.Name == "PUB")
                {
                    lock (pubs)
                        pubs.Add($"PUB {cmd.Subject}");
                }

                if (cmd is { Name: "PUB", Subject: "close" })
                {
                    client.Close();
                }

                return Task.CompletedTask;
            },
            Log,
            info: $"{{\"max_payload\":{1024 * 1024 * 8}}}",
            cancellationToken: cts.Token);

        Log("__________________________________");

        var testLogger = new InMemoryTestLoggerFactory(LogLevel.Error, m =>
        {
            Log($"[NC] {m.Message}");
            if (m.Exception is not SocketException)
                _output.WriteLine($"ERROR: {m.Exception}");
        });

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            LoggerFactory = testLogger,
        });

        Log($"[C] connect {server.Url}");
        await nats.ConnectAsync();

        Log($"[C] ping");
        var rtt = await nats.PingAsync(cts.Token);
        Log($"[C] ping rtt={rtt}");

        Log($"[C] publishing x1...");
        await nats.PublishAsync("x1", "x", cancellationToken: cts.Token);

        // we will close the connection in mock server when we receive subject "close"
        Log($"[C] publishing close (8MB)...");
        var pubTask = nats.PublishAsync("close", new byte[1024 * 1024 * 8], cancellationToken: cts.Token).AsTask();

        await pubTask.WaitAsync(cts.Token);

        for (var i = 1; i <= 10; i++)
        {
            try
            {
                await nats.PingAsync(cts.Token);
                break;
            }
            catch (OperationCanceledException)
            {
                if (i == 10)
                    throw;
                await Task.Delay(10 * i, cts.Token);
            }
        }

        Log($"[C] publishing x2...");
        await nats.PublishAsync("x2", "x", cancellationToken: cts.Token);

        Log($"[C] flush...");
        await nats.PingAsync(cts.Token);

        Assert.Equal(2, testLogger.Logs.Count);
        foreach (var log in testLogger.Logs)
        {
            Assert.True(log.Exception is SocketException, "Socket exception expected");
            var socketErrorCode = (log.Exception as SocketException)!.SocketErrorCode;
            Assert.True(socketErrorCode is SocketError.ConnectionReset or SocketError.Shutdown, "Socket error code");
        }

        lock (pubs)
        {
            Assert.Equal(4, pubs.Count);
            Assert.Equal("PUB x1", pubs[0]);
            Assert.Equal("PUB close", pubs[1]);
            Assert.Equal("PUB close", pubs[2]);
            Assert.Equal("PUB x2", pubs[3]);
        }
    }
}
