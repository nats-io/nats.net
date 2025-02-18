using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core2.Tests;

[Collection("nats-server-restricted-user")]
public class ErrorHandlerTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerRestrictedUserFixture _server;

    public ErrorHandlerTest(ITestOutputHelper output, NatsServerRestrictedUserFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Handle_permissions_violation()
    {
        var logger = new InMemoryTestLoggerFactory(LogLevel.Error);

        var proxy = new NatsProxy(_server.Port);
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{proxy.Port}",
            LoggerFactory = logger,
            AuthOpts = new NatsAuthOpts { Username = "u" },
        });

        var errors = new List<NatsServerErrorEventArgs>();

        nats.ServerError += (_, args) =>
        {
            lock (errors)
            {
                errors.Add(args);
            }

            return default;
        };

        var prefix = _server.GetNextId();

        await nats.PublishAsync("x", $"_{prefix}_published_1_");

        await nats.PingAsync();

        await Retry.Until(
            "published and pinged 1",
            () =>
            {
                var published = false;
                foreach (var frame in proxy.AllFrames)
                {
                    if (frame.Origin == "C" && frame.Message.Contains($"_{prefix}_published_1_"))
                    {
                        published = true;
                        continue;
                    }

                    if (published && frame.Origin == "S" && frame.Message == "PONG")
                    {
                        return true;
                    }
                }

                return false;
            });

        await nats.PublishAsync("y", $"_{prefix}_published_2_");

        await nats.PingAsync();

        await Retry.Until(
            "published and pinged 2",
            () =>
            {
                var published = false;
                foreach (var frame in proxy.AllFrames)
                {
                    if (frame.Origin == "C" && frame.Message.Contains($"_{prefix}_published_2_"))
                    {
                        published = true;
                        continue;
                    }

                    if (published && frame.Origin == "S" && frame.Message == "PONG")
                    {
                        return true;
                    }
                }

                return false;
            });

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Contains(proxy.AllFrames, f => f.Origin == "S" && f.Message == "-ERR 'Permissions Violation for Publish to \"y\"'");

        await Retry.Until(
            "error is logged",
            () =>
            {
                    return logger.Logs.Any(x => x.LogLevel == LogLevel.Error && x.Message == "Server error Permissions Violation for Publish to \"y\"");
            });

        await Retry.Until(
            "server error event",
            () =>
            {
                lock (errors)
                {
                    return errors.Any(e => e.Error == "Permissions Violation for Publish to \"y\"");
                }
            });
    }
}
