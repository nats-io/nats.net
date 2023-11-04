using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Client.Testing.Failground;

public class PubSubTest : ITest
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PubSubTest> _logger;

    public PubSubTest(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<PubSubTest>();
    }

    public async Task Run(CancellationToken cancellationToken = default)
    {
        var natsOpts = NatsOpts.Default with
        {
            Url = "nats://192.168.0.183:4222",
            /* Url = "nats://127.0.0.1:4222", */
            LoggerFactory = _loggerFactory,
        };

        await using var nats = new NatsConnection(natsOpts);

        await using var sub = await nats.SubscribeAsync<string>("data", cancellationToken: cancellationToken);

        var subTask = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var msg = await sub.Msgs.ReadAsync(cancellationToken);
                    _logger.LogInformation($"[SUB] Received: {msg.Data}");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Loop error");
            }
        });

        var pubTask = Task.Run(async () =>
        {
            for (var i = 0; ; i++)
            {
                await Task.Delay(500, cancellationToken);
                await nats.PublishAsync("data", $"data_[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff}]_{i:D5}", cancellationToken: cancellationToken);
            }
        });

        _logger.LogInformation($"End of test");

        await Task.WhenAll(subTask, pubTask);
    }
}
