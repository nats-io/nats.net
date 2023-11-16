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

        await using var nats1 = new NatsConnection(natsOpts);
        await using var nats2 = new NatsConnection(natsOpts);

        nats1.ConnectionDisconnected += (_, _) => _logger.LogWarning($"[CON-1] Disconnected");
        nats1.ConnectionOpened += (_, _) => _logger.LogInformation($"[CON-1] Connected to {nats1.ServerInfo?.Name}");

        nats2.ConnectionDisconnected += (_, _) => _logger.LogWarning($"[CON-2] Disconnected");
        nats2.ConnectionOpened += (_, _) => _logger.LogInformation($"[CON-2] Connected to {nats2.ServerInfo?.Name}");

        await nats1.ConnectAsync();
        await nats2.ConnectAsync();

        var js1 = new NatsJSContext(nats1);
        var js2 = new NatsJSContext(nats2);

        var stream = await js1.CreateStreamAsync(
            new StreamConfig(name: "s1", subjects: new[] { "s1.*" }) { NumReplicas = 3 },
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

                                var ack = await js2.PublishAsync(
                                    subject: "s1.x",
                                    data: i,
                                    opts: opts,
                                    cancellationToken: cts.Token);
                                ack.EnsureSuccess();

                                await File.AppendAllTextAsync($"test_publish.txt", $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff} [SND] ({i})\n", cts.Token);

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

                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
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

        var consumer = await js1.CreateOrderedConsumerAsync("s1", cancellationToken: cancellationToken);

        _logger.LogInformation("Created ordered consumer");

        try
        {
            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: new NatsJSConsumeOpts { MaxMsgs = 100 }, cancellationToken: cts.Token))
            {
                if (count != msg.Data)
                    throw new Exception($"Unordered {count} != {msg.Data}");

                await File.AppendAllTextAsync($"test_consume.txt", $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff} [RCV] ({count}) {msg.Subject}: {msg.Data}\n", cts.Token);
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
