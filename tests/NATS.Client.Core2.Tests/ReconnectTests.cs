using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core2.Tests;

public class ReconnectTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Reconnect_on_max_connections()
    {
        var logger = new InMemoryTestLoggerFactory(LogLevel.Warning, m => output.WriteLine($"LOG: {m.Message}"));
        var confFile = $"{nameof(Reconnect_on_max_connections)}_server.conf";
        {
            var config = """
                         accounts: {
                           $SYS { users [{user: sys, password: sys}]}
                         }
                         max_connections: 2
                         """;
            File.WriteAllText(path: confFile, contents: config);
        }

        await using var server = await NatsServerProcess.StartAsync(config: confFile, withJs: false);

        await using var nats1 = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logger, AuthOpts = new NatsAuthOpts { Username = "sys", Password = "sys" } });
        await nats1.ConnectRetryAsync();

        await using var nats2 = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logger, AuthOpts = new NatsAuthOpts { Username = "sys", Password = "sys" } });
        await nats2.ConnectRetryAsync();

        await using var nats3 = new NatsConnection(new NatsOpts { Url = server.Url, AuthOpts = new NatsAuthOpts { Username = "sys", Password = "sys" } });
        await Assert.ThrowsAsync<NatsException>(async () => await nats3.ConnectAsync());

        // Reload the server configuration to change max connections to 1
        {
            var config = """
                         accounts: {
                           $SYS { users [{user: sys, password: sys}]}
                         }
                         max_connections: 1
                         """;
            File.WriteAllText(path: confFile, contents: config);
        }

        _ = Task.Run(async () => await nats1.RequestAsync<string>($"$SYS.REQ.SERVER.{nats1.ServerInfo!.Id}.RELOAD"));

        await Retry.Until(
            "warning",
            () => logger.Logs.Any(l => l.Message.Contains("maximum connections exceeded")));

        // Reload the server configuration to change max connections back to 2
        {
            var config = """
                         accounts: {
                           $SYS { users [{user: sys, password: sys}]}
                         }
                         max_connections: 2
                         """;
            File.WriteAllText(path: confFile, contents: config);
        }

        _ = Task.Run(async () => await nats1.RequestAsync<string>($"$SYS.REQ.SERVER.{nats1.ServerInfo!.Id}.RELOAD"));
        _ = Task.Run(async () => await nats2.RequestAsync<string>($"$SYS.REQ.SERVER.{nats2.ServerInfo!.Id}.RELOAD"));

        await Retry.Until(
            "one client reconnects and both connections are open",
            () => nats1.ConnectionState == NatsConnectionState.Open && nats2.ConnectionState == NatsConnectionState.Open);
    }
}
