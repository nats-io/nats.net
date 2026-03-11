using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

public class ProtocolTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(1024 * 1024)]
    public async Task Protocol_parser_under_load(int size)
    {
        var server = await NatsServerProcess.StartAsync();
        var logger = new InMemoryTestLoggerFactory(LogLevel.Error);
        var opts = new NatsOpts { Url = server.Url, LoggerFactory = logger };
        var nats = new NatsConnection(opts);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var signal = new WaitSignal();
        var counts = 0;
        var subscribeTask = Task.Run(
            async () =>
            {
                var count = 0;
                var last = string.Empty;
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await foreach (var msg in nats.SubscribeAsync<byte[]>("x.*", cancellationToken: cts.Token))
                        {
                            if (++count > 100)
                                signal.Pulse();

                            if (last != msg.Subject)
                            {
                                last = msg.Subject;
                                Interlocked.Increment(ref counts);
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
            },
            cts.Token);

        var r = 0;
        var payload = new byte[size];
        var publishTask = Task.Run(
            async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await nats.PublishAsync($"x.{Interlocked.CompareExchange(ref r, 0, 0)}", payload, cancellationToken: cts.Token);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            },
            cts.Token);

        await signal;

        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(1_000, cts.Token);
            var subjectCount = Volatile.Read(ref counts);
            server = await server.RestartAsync();

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await new NatsConnection(new NatsOpts { Url = server.Url }).PingAsync(cts.Token);
                    break;
                }
                catch
                {
                    // ignored
                }
            }

            Interlocked.Increment(ref r);

            await Retry.Until("subject count goes up", () => Volatile.Read(ref counts) > subjectCount, timeout: TimeSpan.FromSeconds(60));
        }

        cts.Cancel();
        await subscribeTask;
        await publishTask;

        foreach (var log in logger.Logs.Where(x => x.EventId == NatsLogEvents.Protocol && x.LogLevel == LogLevel.Error))
        {
            Assert.DoesNotContain("Unknown Protocol Operation", log.Message);
        }

        counts.Should().BeGreaterOrEqualTo(3);
    }
}
