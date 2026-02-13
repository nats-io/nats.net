using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;
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

        await nats.ConnectAsync();

        var connectLog = logger.Logs.FirstOrDefault(m => m.Message.Contains("Connect to NATS using"));
        Assert.NotNull(connectLog);
        Assert.Contains($"127.0.0.1:{serverPort}", connectLog.Message);
        Assert.DoesNotContain("original-host", connectLog.Message);
    }
}
