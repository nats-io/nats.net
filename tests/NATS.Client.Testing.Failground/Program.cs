using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Testing.Failground;

try
{
    var cmdArgs = CmdArgs.Parse(args);

    if (cmdArgs.HasError)
    {
        Console.Error.WriteLine("Error: " + cmdArgs.Error);
        return 2;
    }

    var runId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}";

    using var loggerFactory = LoggerFactory.Create(configure: builder =>
    {
        builder
            .SetMinimumLevel(cmdArgs.LogLevel)
            .AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
            });
    });

    var logger = loggerFactory.CreateLogger("Program");
    logger.LogInformation("Starting...");

    var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        if (!cts.IsCancellationRequested)
        {
            e.Cancel = true;
            Console.Error.WriteLine("Stopping...");
            cts.Cancel();
        }
        else
        {
            Console.Error.WriteLine("Aborting...");
        }
    };

    var tests = new Dictionary<string, ITest>
    {
        { "consumer", new ConsumeTest(loggerFactory) },
        { "ordered-consumer", new OrderedConsumeTest(loggerFactory) },
        { "pub-sub", new PubSubTest(loggerFactory) },
        { "stay-connected", new StayConnectedTest(loggerFactory) },
    };

    if (cmdArgs.Workload == null || !tests.TryGetValue(cmdArgs.Workload, out var test))
    {
        Console.Error.WriteLine($"Error: can't find workload '{cmdArgs.Workload}'");
        Console.Error.WriteLine("  Available workloads: " + string.Join(", ", tests.Keys));
        return 2;
    }

    try
    {
        logger.LogDebug("Starting health checks ({Id})...", cmdArgs.Id);

        _ = Task.Run(async () =>
        {
            var natsOpts = NatsOpts.Default;

            if (cmdArgs.Server != null)
            {
                natsOpts = natsOpts with { Url = cmdArgs.Server };
            }

            await using var nats = new NatsConnection(natsOpts);

            var subject = $"healthcheck.{cmdArgs.Id}";

            logger.LogTrace("Subscribing to {Subject}...", subject);

            await foreach (var msg in nats.SubscribeAsync<NatsMemoryOwner<byte>>(subject))
            {
                if (msg.Data.Length > 0)
                {
                    var buffer = NatsMemoryOwner<byte>.Allocate(msg.Data.Length);

                    using (var memoryOwner = msg.Data)
                        memoryOwner.Memory.CopyTo(buffer.Memory);

                    logger.LogDebug("Sending health check response...");
                    await msg.ReplyAsync(buffer);
                }
                else
                {
                    logger.LogError("Health check failed: no data received");
                }
            }
        });

        logger.LogInformation("Starting test {Name} ({RunId})...", test.GetType().Name, runId);

        await test.Run(runId, cmdArgs, cts.Token);

        return 0;
    }
    catch (Exception e)
    {
        logger.LogError(e, "Error running test {Name}", test.GetType().Name);
        return 1;
    }
}
catch (Exception e)
{
    Console.Error.WriteLine("Unexpected error: " + e.Message);
    return 1;
}
