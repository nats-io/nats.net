using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NATS.Client.Testing.Failground;

[SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1515:Single-line comment should be preceded by blank line")]
[SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1404:Code analysis suppression should have justification")]
public class OrderedConsumeTest : ITest
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OrderedConsumeTest> _logger;

    public OrderedConsumeTest(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<OrderedConsumeTest>();
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
                        for (var j = 0; j < 10; j++)
                        {
                            try
                            {
                                var opts = new NatsJSPubOpts { MsgId = $"{i}" };

                                if (i > 0)
                                    opts = opts with { ExpectedLastMsgId = $"{i - 1}" };

                                var ack = await js.PublishAsync(
                                    subject: "s1.x",
                                    data: i,
                                    opts: opts,
                                    cancellationToken: cts.Token);
                                ack.EnsureSuccess();

                                await File.AppendAllTextAsync($"test_{runId}_publish.txt", $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff} [SND] ({i})\n", cts.Token);

                                break;
                            }
                            catch (NatsJSDuplicateMessageException)
                            {
                                _logger.LogWarning("Publish duplicate. Ignoring...");
                                break;
                            }
                            catch (NatsJSPublishNoResponseException)
                            {
                                _logger.LogWarning($"Publish no response. Retrying({j + 1}/10)...");
                            }
                        }
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

        var consumer = await js.CreateOrderedConsumerAsync("s1", cancellationToken: cancellationToken);

        _logger.LogInformation("Created consumer {Name}", consumer.Info.Config.Name);

        try
        {
            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
            {
                if (count != msg.Data)
                    throw new Exception($"Unordered {count} != {msg.Data}");

                await File.AppendAllTextAsync($"test_{runId}_consume.txt", $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff} [RCV] ({count}) {msg.Subject}: {msg.Data}\n", cts.Token);
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
