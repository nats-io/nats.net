using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Testing.Failground;

public class StayConnectedTest : ITest
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PubSubTest> _logger;

    public StayConnectedTest(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<PubSubTest>();
    }

    public async Task Run(string runId, CmdArgs args, CancellationToken cancellationToken = default)
    {
        var natsOpts = NatsOpts.Default with { LoggerFactory = _loggerFactory };

        if (args.Server != null)
        {
            natsOpts = natsOpts with { Url = args.Server };
        }

        await using var nats = new NatsConnection(natsOpts);

        nats.ConnectionDisconnected += (_, _) => _logger.LogWarning($"[CON] Disconnected");
        nats.ConnectionOpened += (_, _) => _logger.LogInformation($"[CON] Connected to {nats.ServerInfo?.Name}");

        var maxRetry = args.MaxRetry > 0 ? args.MaxRetry : 10_000;

        // Connect
        {
            var stopwatch = Stopwatch.StartNew();
            var connected = false;
            while (stopwatch.ElapsedMilliseconds < maxRetry)
            {
                try
                {
                    await nats.PingAsync(cancellationToken);
                    connected = true;
                    break;
                }
                catch (NatsException e)
                {
                    _logger.LogWarning(e, "Ping error. retrying...");
                }
            }

            if (!connected)
                throw new Exception("Failed to connect");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(100, cancellationToken);

            if (nats.ConnectionState == NatsConnectionState.Open)
                continue;

            _logger.LogWarning("Disconnected. Reconnecting...");

            var stopwatch = Stopwatch.StartNew();
            var connected = false;
            while (!cancellationToken.IsCancellationRequested && stopwatch.ElapsedMilliseconds < maxRetry)
            {
                if (nats.ConnectionState == NatsConnectionState.Open)
                {
                    _logger.LogInformation("Connected!");
                    connected = true;
                    break;
                }
            }

            if (!connected)
                throw new Exception("Failed to connect");
        }
    }
}
