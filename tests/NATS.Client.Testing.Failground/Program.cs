using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NATS.Client.Testing.Failground;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddConsole();
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
    { "pub-sub", new PubSubTest(loggerFactory) },
};

if (args.Length != 1 || !tests.TryGetValue(args[0], out var test))
{
    Console.Error.WriteLine($"Usage: {Process.GetCurrentProcess().ProcessName} <test>");
    Console.Error.WriteLine("  Available tests: " + string.Join(", ", tests.Keys));
    return 2;
}

try
{
    logger.LogInformation("Running test {Name}...", test.GetType().Name);
    await test.Run(cts.Token);
    return 0;
}
catch (Exception e)
{
    logger.LogError(e, "Error running test {Name}", test.GetType().Name);
    return 1;
}
