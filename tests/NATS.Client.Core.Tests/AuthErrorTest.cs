using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

public class AuthErrorTest
{
    private readonly ITestOutputHelper _output;

    public AuthErrorTest(ITestOutputHelper output) => _output = output;

    // [SkipOnPlatform("WINDOWS", "doesn't support HUP signal")]
    [Fact]
    public async Task Auth_err_twice_will_stop_retries()
    {
        var authErrCount = 0;
        var stopCount = 0;

        var confFile = $"{nameof(Auth_err_can_be_ignored_for_retires)}_server.conf";
        var confContents = """
                           authorization: {
                               users: [
                                   {user: a, password: b}
                               ]
                           }
                           """;
        File.WriteAllText(path: confFile, contents: confContents);
        var server = await NatsServerProcess.StartAsync(config: confFile);
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            AuthOpts = new NatsAuthOpts { Username = "a", Password = "b", },
            IgnoreAuthErrorAbort = false,
            LoggerFactory = new InMemoryTestLoggerFactory(
                LogLevel.Warning,
                log =>
                {
                    _output.WriteLine($"LOG: {log.LogLevel} - {log.Message}");
                    if (log.LogLevel == LogLevel.Warning && log.Message.StartsWith("Authentication error:"))
                    {
                        Interlocked.Increment(ref authErrCount);
                    }
                    else if (log.LogLevel == LogLevel.Error && log.Message.StartsWith("Received same authentication error"))
                    {
                        Interlocked.Increment(ref stopCount);
                    }
                }),
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Successful connection
        {
            _output.WriteLine($"Starting at {server.Url}");
            var rtt = await nats.PingAsync(cts.Token);
            _output.WriteLine($"Ping at {server.Url} took {rtt}");
            Assert.True(rtt > TimeSpan.Zero);
        }

        // Reload config with different password
        {
            var conf = File.ReadAllText(server.Config!)
                .Replace("password: b", "password: c");
            File.WriteAllText(server.Config!, conf);
            await Task.Delay(1000, cts.Token);

            // Process.Start("kill", $"-HUP {server.Pid}");
            _output.WriteLine($"Reloading config with different password");
            server = await server.RestartAsync();
        }

        _output.WriteLine($"Stopping at {server.Url}");
        await Retry.Until("stopped", () => Volatile.Read(ref stopCount) == 1);
        Assert.Equal(2, Volatile.Read(ref authErrCount));

        await server.DisposeAsync();
    }

    // [SkipOnPlatform("WINDOWS", "doesn't support HUP signal")]
    [Fact]
    public async Task Auth_err_can_be_ignored_for_retires()
    {
        var authErrCount = 0;
        var stopCount = 0;

        var confFile = $"{nameof(Auth_err_can_be_ignored_for_retires)}_server.conf";
        var confContents = """
                           authorization: {
                               users: [
                                   {user: a, password: b}
                               ]
                           }
                           """;
        File.WriteAllText(path: confFile, contents: confContents);
        var server = await NatsServerProcess.StartAsync(config: confFile);
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            AuthOpts = new NatsAuthOpts { Username = "a", Password = "b", },
            IgnoreAuthErrorAbort = true,
            LoggerFactory = new InMemoryTestLoggerFactory(
                LogLevel.Warning,
                log =>
                {
                    if (log.LogLevel == LogLevel.Warning && log.Message.StartsWith("Authentication error:"))
                    {
                        Interlocked.Increment(ref authErrCount);
                    }
                    else if (log.LogLevel == LogLevel.Error && log.Message.StartsWith("Received same authentication error"))
                    {
                        Interlocked.Increment(ref stopCount);
                    }
                }),
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Successful connection
        {
            var rtt = await nats.PingAsync(cts.Token);
            Assert.True(rtt > TimeSpan.Zero);
        }

        // Reload config with different password
        {
            var conf = File.ReadAllText(server.Config!)
                .Replace("password: b", "password: c");
            File.WriteAllText(server.Config!, conf);
            await Task.Delay(1000, cts.Token);

            // Process.Start("kill", $"-HUP {server.Pid}");
            server = await server.RestartAsync();
        }

        await Retry.Until("stopped", () => Volatile.Read(ref authErrCount) > 3, timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(0, Volatile.Read(ref stopCount));
        Assert.True(Volatile.Read(ref authErrCount) > 3);

        // Reload config with correct password
        {
            var conf = File.ReadAllText(server.Config!)
                .Replace("password: c", "password: b");
            File.WriteAllText(server.Config!, conf);
            await Task.Delay(1000, cts.Token);

            // Process.Start("kill", $"-HUP {server.Pid}");
            server = await server.RestartAsync();
        }

        // Reconnected successfully
        {
            var rtt = await nats.PingAsync(cts.Token);
            Assert.True(rtt > TimeSpan.Zero);
        }

        await server.DisposeAsync();
    }

    [Fact]
    public async Task Auth_err_then_connection_recreation_does_not_cause_unobserved_exception()
    {
        // Arrange
        var exceptionThrown = false;

        var confFile = $"{nameof(Auth_err_then_connection_recreation_does_not_cause_unobserved_exception)}_server.conf";
        var confContents = """
                           authorization: {
                               users: [
                                   {user: a, password: b}
                               ]
                           }
                           """;

        // Act
        TaskScheduler.UnobservedTaskException += (_, _) =>
        {
            exceptionThrown = true;
        };

        File.WriteAllText(path: confFile, contents: confContents);

        var server = await NatsServerProcess.StartAsync(config: confFile);

        await Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var conn = new NatsConnection(new NatsOpts
                {
                    Url = server.Url,
                });

                try
                {
                    await conn.ConnectAsync();
                }
                catch (NatsException)
                {
                }
                finally
                {
                    await conn.DisposeAsync();
                }
            }
        });

        await Task.Delay(100);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Assert
        Assert.False(exceptionThrown);
    }
}
