using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NATS.Client.Testing.Failground;

[SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1515:Single-line comment should be preceded by blank line")]
[SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1404:Code analysis suppression should have justification")]
public class ConsumeTest : ITest
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ConsumeTest> _logger;

    public ConsumeTest(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<ConsumeTest>();
    }

    public async Task Run(CancellationToken cancellationToken = default)
    {
        var natsOpts = NatsOpts.Default with
        {
            Url = "nats://192.168.0.183:4222",
            // Url = "nats://127.0.0.1:4222",
            LoggerFactory = _loggerFactory,
        };

        await using var nats = new NatsConnection(natsOpts);

        nats.ConnectionOpened += (_, _) => _logger.LogInformation($"[CON] Connected to {nats.ServerInfo?.Name}");

        await nats.ConnectAsync();

        var js = new NatsJSContext(nats);

        var stream = await js.CreateStreamAsync(
            new StreamConfiguration
            {
                Name = "s1",
                Subjects = new[] { "s1.*" },
                NumReplicas = 3,
            },
            cancellationToken);

        _logger.LogInformation("Created stream {Name}", stream.Info.Config.Name);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var publisher = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Starting publishing...");
                for (var i = 0; ; i++)
                {
                    try
                    {
                        var cts0 = new CancellationTokenSource(30_000);
                        var cts1 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cts0.Token);
                        await js.PublishAsync(subject: "s1.x", data: $"data_[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff}]_{i:D5}", cancellationToken: cts1.Token);
                        if (i % 100 == 0)
                            _logger.LogInformation($"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff} [SND] ({i})");
                    }
                    catch (NatsJSPublishNoResponseException)
                    {
                        _logger.LogInformation("Publish no response. Retrying...");
                    }
                    catch (NatsJSException e)
                    {
                        _logger.LogError(e, "Publish error");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(.5), cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Publish loop error");
            }
            finally
            {
                await cts.CancelAsync();
            }
        });

        var consumer = await js.CreateConsumerAsync(
            new ConsumerCreateRequest
            {
                StreamName = "s1",
                Config = new ConsumerConfiguration
                {
                    Name = "c1",
                    DurableName = "c1",
                    AckPolicy = ConsumerConfigurationAckPolicy.@explicit,
                    NumReplicas = 3,
                },
            },
            cancellationToken);

        _logger.LogInformation("Created consumer {Name}", consumer.Info.Config.Name);

        try
        {
            var count = 0;
            await foreach (var msg in consumer.ConsumeAllAsync<string>(cancellationToken: cts.Token))
            {
                if (count % 100 == 0)
                    _logger.LogInformation($"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff} [RCV] ({count}) {msg.Subject}: {msg.Data}");
                await msg.AckAsync(cancellationToken: cts.Token);
                count++;
            }
        }
        catch (OperationCanceledException)
        {
        }

        _logger.LogInformation("Bye");

        await publisher;
    }
}
