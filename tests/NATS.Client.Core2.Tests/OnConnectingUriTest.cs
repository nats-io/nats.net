using Microsoft.Extensions.Logging;
using NATS.Client.Platform.Windows.Tests;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

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
}
