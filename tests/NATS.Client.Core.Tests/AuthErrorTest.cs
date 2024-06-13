using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Tests;

public class AuthErrorTest
{
    private readonly ITestOutputHelper _output;

    public AuthErrorTest(ITestOutputHelper output) => _output = output;

    [SkipOnPlatform("WINDOWS", "doesn't support HUP signal")]
    public async Task Auth_err_twice_will_stop_retries()
    {
        await using var server = NatsServer.Start(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .AddServerConfigText(@"
authorization: {
    users: [
        {user: a, password: b}
    ]
}
")
                .Build(),
            new NatsOpts
            {
                AuthOpts = new NatsAuthOpts
                {
                    Username = "a",
                    Password = "b",
                },
            });

        await using var nats = server.CreateClientConnection(new NatsOpts
        {
            AuthOpts = new NatsAuthOpts
            {
                Username = "a",
                Password = "b",
            },
        });

        var authErrCount = 0;
        var stopCount = 0;
        server.OnLog += log =>
        {
            if (log.LogLevel == LogLevel.Warning && log.Text.StartsWith("Authentication error:"))
            {
                Interlocked.Increment(ref authErrCount);
            }
            else if (log.LogLevel == LogLevel.Error && log.Text.StartsWith("Received same authentication error"))
            {
                Interlocked.Increment(ref stopCount);
            }
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Successful connection
        {
            var rtt = await nats.PingAsync(cts.Token);
            Assert.True(rtt > TimeSpan.Zero);
        }

        // Reload config with different password
        {
            var conf = (await File.ReadAllTextAsync(server.ConfigFile!, cts.Token))
                .Replace("password: b", "password: c");
            await File.WriteAllTextAsync(server.ConfigFile!, conf, cts.Token);
            await Task.Delay(1000, cts.Token);
            Process.Start("kill", $"-HUP {server.ServerProcess!.Id}");
        }

        await Retry.Until("stopped", () => Volatile.Read(ref stopCount) == 1);
        Assert.Equal(2, Volatile.Read(ref authErrCount));
    }

    [SkipOnPlatform("WINDOWS", "doesn't support HUP signal")]
    public async Task Auth_err_can_be_ignored_for_retires()
    {
        await using var server = NatsServer.Start(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .AddServerConfigText(@"
authorization: {
    users: [
        {user: a, password: b}
    ]
}
")
                .Build(),
            new NatsOpts
            {
                AuthOpts = new NatsAuthOpts
                {
                    Username = "a",
                    Password = "b",
                },
            });

        await using var nats = server.CreateClientConnection(new NatsOpts
        {
            AuthOpts = new NatsAuthOpts
            {
                Username = "a",
                Password = "b",
            },
            IgnoreAuthErrorAbort = true,
        });

        var authErrCount = 0;
        var stopCount = 0;
        server.OnLog += log =>
        {
            if (log.LogLevel == LogLevel.Warning && log.Text.StartsWith("Authentication error:"))
            {
                Interlocked.Increment(ref authErrCount);
            }
            else if (log.LogLevel == LogLevel.Error && log.Text.StartsWith("Received same authentication error"))
            {
                Interlocked.Increment(ref stopCount);
            }
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Successful connection
        {
            var rtt = await nats.PingAsync(cts.Token);
            Assert.True(rtt > TimeSpan.Zero);
        }

        // Reload config with different password
        {
            var conf = (await File.ReadAllTextAsync(server.ConfigFile!, cts.Token))
                .Replace("password: b", "password: c");
            await File.WriteAllTextAsync(server.ConfigFile!, conf, cts.Token);
            await Task.Delay(1000, cts.Token);
            Process.Start("kill", $"-HUP {server.ServerProcess!.Id}");
        }

        await Retry.Until("stopped", () => Volatile.Read(ref authErrCount) > 3, timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(0, Volatile.Read(ref stopCount));
        Assert.True(Volatile.Read(ref authErrCount) > 3);

        // Reload config with correct password
        {
            var conf = (await File.ReadAllTextAsync(server.ConfigFile!, cts.Token))
                .Replace("password: c", "password: b");
            await File.WriteAllTextAsync(server.ConfigFile!, conf, cts.Token);
            await Task.Delay(1000, cts.Token);
            Process.Start("kill", $"-HUP {server.ServerProcess!.Id}");
        }

        // Reconnected successfully
        {
            var rtt = await nats.PingAsync(cts.Token);
            Assert.True(rtt > TimeSpan.Zero);
        }
    }
}
