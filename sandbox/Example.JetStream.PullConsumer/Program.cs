using System.Diagnostics;
using Example.JetStream.PullConsumer;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var options = NatsOpts.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

await using var nats = new NatsConnection(options);

var js = new NatsJSContext(nats);

var consumer = await js.CreateConsumerAsync("s1", "c1");

var idle = TimeSpan.FromSeconds(15);
var expires = TimeSpan.FromSeconds(30);

// int? maxMsgs = null;
// int? maxBytes = 128;
int? maxMsgs = 1000;
int? maxBytes = null;

static void ErrorHandler(NatsJSNotification notification)
{
    Console.WriteLine($"Error: {notification}");
}

void Report(int i, Stopwatch sw, string data)
{
    Console.WriteLine(data);
    if (i % 10000 == 0)
        Console.WriteLine($"Received: {i / sw.Elapsed.TotalSeconds:f2} msgs/s [{i}] {sw.Elapsed}");
}

var consumeOpts = new NatsJSConsumeOpts
{
    MaxMsgs = maxMsgs,
    MaxBytes = maxBytes,
    Expires = expires,
    IdleHeartbeat = idle,
    Serializer = new RawDataSerializer(),
    ErrorHandler = ErrorHandler,
};

var fetchOpts = new NatsJSFetchOpts
{
    MaxMsgs = maxMsgs,
    MaxBytes = maxBytes,
    Expires = expires,
    IdleHeartbeat = idle,
    Serializer = new RawDataSerializer(),
    ErrorHandler = ErrorHandler,
};

var nextOpts = new NatsJSNextOpts
{
    Expires = expires,
    IdleHeartbeat = idle,
    Serializer = new RawDataSerializer(),
    ErrorHandler = ErrorHandler,
};

var stopwatch = Stopwatch.StartNew();
var count = 0;

try
{
    if (args.Length > 0 && args[0] == "fetch")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine($"___\nFETCH {maxMsgs}");
            await using var sub = await consumer.FetchAsync<RawData>(fetchOpts, cts.Token);
            await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                Report(++count, stopwatch, $"data: {msg.Data}");
            }
        }
    }
    else if (args.Length > 0 && args[0] == "fetch-all")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine($"___\nFETCH {maxMsgs}");
            await foreach (var msg in consumer.FetchAllAsync<RawData>(fetchOpts, cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                Report(++count, stopwatch, $"data: {msg.Data}");
            }
        }
    }
    else if (args.Length > 0 && args[0] == "next")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine("___\nNEXT");
            var next = await consumer.NextAsync<RawData>(nextOpts, cts.Token);
            if (next is { } msg)
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                Report(++count, stopwatch, $"data: {msg.Data}");
            }
        }
    }
    else if (args.Length > 0 && args[0] == "consume")
    {
        Console.WriteLine("___\nCONSUME");
        await using var sub = await consumer.ConsumeAsync<RawData>(
            consumeOpts,
            cts.Token);

        await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Report(++count, stopwatch, $"data: {msg.Data}");
        }

        // Console.WriteLine($"took {stopwatch.Elapsed}");
        // await nats.PingAsync(cts.Token);
    }
    else if (args.Length > 0 && args[0] == "consume-all")
    {
        Console.WriteLine("___\nCONSUME-ALL");
        await foreach (var msg in consumer.ConsumeAllAsync<RawData>(consumeOpts, cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Report(++count, stopwatch, $"data: {msg.Data}");
        }
    }
    else
    {
        Console.WriteLine("Usage: dotnet run -- <consume|consume-all|fetch|fetch-all|next>");
    }
}
catch (OperationCanceledException)
{
}

Console.WriteLine("Bye");
