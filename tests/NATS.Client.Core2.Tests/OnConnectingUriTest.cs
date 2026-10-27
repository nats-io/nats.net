using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core2.Tests;

public class OnConnectingUriTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Logs_show_overridden_uri_when_OnConnectingAsync_changes_host_port()
    {
        await using var server = await NatsServerProcess.StartAsync();
        var serverPort = server.Port;

        var logger = new InMemoryTestLoggerFactory(LogLevel.Information, m => output.WriteLine($"LOG: {m.Message}"));

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = "nats://original-host:4222",
            LoggerFactory = logger,
        });

        nats.OnConnectingAsync = _ => new ValueTask<(string Host, int Port)>(("127.0.0.1", serverPort));

        await nats.ConnectRetryAsync();

        var connectLog = logger.Logs.FirstOrDefault(m => m.Message.Contains("Connect to NATS using"));
        Assert.NotNull(connectLog);
        Assert.Contains($"127.0.0.1:{serverPort}", connectLog.Message);
        Assert.DoesNotContain("original-host", connectLog.Message);
    }

    [Fact]
    public async Task Error_reports_overridden_uri_when_OnConnectingAsync_changes_host_port()
    {
        var logger = new InMemoryTestLoggerFactory(LogLevel.Error, m => output.WriteLine($"LOG: {m.Message}"));

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = "nats://original-host:4222",
            LoggerFactory = logger,
            ConnectTimeout = TimeSpan.FromSeconds(2),
        });

        nats.OnConnectingAsync = _ => new ValueTask<(string Host, int Port)>(("rewritten-host", 9999));

        var exception = await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());

        Assert.Contains("rewritten-host:9999", exception.Message);
        Assert.DoesNotContain("original-host", exception.Message);

        var errorLog = logger.Logs.FirstOrDefault(m => m.Message.Contains("Fail to connect NATS"));
        Assert.NotNull(errorLog);
        Assert.Contains("rewritten-host:9999", errorLog.Message);
        Assert.DoesNotContain("original-host", errorLog.Message);
    }

    [Fact]
    public async Task ConnectionOpened_reports_overridden_uri_on_initial_connect()
    {
        await using var server = await NatsServerProcess.StartAsync();
        var serverPort = server.Port;

        await using var nats = new NatsConnection(new NatsOpts { Url = "nats://original-host:4222" });
        nats.OnConnectingAsync = _ => new ValueTask<(string Host, int Port)>(("127.0.0.1", serverPort));

        var opened = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        nats.ConnectionOpened += (_, e) =>
        {
            opened.TrySetResult(e.Message);
            return default;
        };

        await nats.ConnectRetryAsync();

        var message = await WithTimeout(opened.Task, TimeSpan.FromSeconds(10));
        Assert.Contains($"127.0.0.1:{serverPort}", message);
        Assert.DoesNotContain("original-host", message);
    }

    [Fact]
    public async Task ConnectionOpened_reports_overridden_uri_on_reconnect()
    {
        await using var serverA = await NatsServerProcess.StartAsync();
        await using var serverB = await NatsServerProcess.StartAsync();
        var portA = serverA.Port;
        var portB = serverB.Port;

        var logger = new InMemoryTestLoggerFactory(LogLevel.Information, m => output.WriteLine($"LOG: {m.Message}"));

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = "nats://original-host:4222",
            LoggerFactory = logger,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(200),
        });

        var target = portA;
        nats.OnConnectingAsync = _ => new ValueTask<(string Host, int Port)>(("127.0.0.1", Volatile.Read(ref target)));

        var firstOpened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reopened = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opens = 0;
        nats.ConnectionOpened += (_, e) =>
        {
            if (Interlocked.Increment(ref opens) == 1)
                firstOpened.TrySetResult(true);
            else
                reopened.TrySetResult(e.Message);
            return default;
        };

        await nats.ConnectRetryAsync();
        await WithTimeout(firstOpened.Task, TimeSpan.FromSeconds(10));

        Volatile.Write(ref target, portB);
        await serverA.DisposeAsync();

        var message = await WithTimeout(reopened.Task, TimeSpan.FromSeconds(30));
        Assert.Contains($"127.0.0.1:{portB}", message);
        Assert.DoesNotContain("original-host", message);

        // The success log is written before the event is queued; the last one belongs to the reconnect.
        var successLog = logger.Logs.Last(m => m.Message.Contains("Connection succeeded"));
        Assert.Contains($"127.0.0.1:{portB}", successLog.Message);
        Assert.DoesNotContain("original-host", successLog.Message);
    }

    // Task<T>.WaitAsync is not available on net481.
    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task)
        {
            throw new TimeoutException("The operation has timed out.");
        }

        return await task.ConfigureAwait(false);
    }
}
