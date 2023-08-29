using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var options = NatsOptions.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Debug) };

await using var nats = new NatsConnection(options);

var js = new NatsJSContext(nats);

var consumer = await js.CreateConsumerAsync("s1", "c1");

var idle = TimeSpan.FromSeconds(1);
var expires = TimeSpan.FromSeconds(10);

// int? maxMsgs = null;
// int? maxBytes = 128;
int? maxMsgs = 10;
int? maxBytes = null;

static void ErrorHandler(NatsJSNotification notification)
{
    Console.WriteLine($"Error: {notification}");
}

var fetchOpts = new NatsJSFetchOpts
{
    MaxMsgs = maxMsgs,
    MaxBytes = maxBytes,
    Expires = expires,
    IdleHeartbeat = idle,
    Serializer = new RawDataSerializer(),
    ErrorHandler = ErrorHandler,
};

var consumeOpts = new NatsJSConsumeOpts
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

try
{
    if (args.Length > 0 && args[0] == "fetch")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine($"___\nFETCH {maxMsgs}");
            await using var sub = await consumer.FetchAsync<RawData>(fetchOpts, cts.Token);
            await foreach (var jsMsg in sub.Msgs.ReadAllAsync(cts.Token))
            {
                var msg = jsMsg.Msg;
                Console.WriteLine($"data: {msg.Data}");
                await jsMsg.AckAsync(cts.Token);
            }
        }
    }
    else if (args.Length > 0 && args[0] == "fetch-all")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine($"___\nFETCH {maxMsgs}");
            await foreach (var jsMsg in consumer.FetchAllAsync<RawData>(fetchOpts, cts.Token))
            {
                var msg = jsMsg.Msg;
                Console.WriteLine($"data: {msg.Data}");
                await jsMsg.AckAsync(cts.Token);
            }
        }
    }
    else if (args.Length > 0 && args[0] == "next")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine("___\nNEXT");
            var next = await consumer.NextAsync<RawData>(nextOpts, cts.Token);
            if (next is { } jsMsg)
            {
                var msg = jsMsg.Msg;
                Console.WriteLine($"data: {msg.Data}");
                await jsMsg.AckAsync(cts.Token);
            }
        }
    }
    else if (args.Length > 0 && args[0] == "consume")
    {
        Console.WriteLine("___\nCONSUME");
        await using var sub = await consumer.ConsumeAsync<RawData>(consumeOpts, cts.Token);
        await foreach (var jsMsg in sub.Msgs.ReadAllAsync(cts.Token))
        {
            var msg = jsMsg.Msg;
            Console.WriteLine($"data: {msg.Data}");
            await jsMsg.AckAsync(cts.Token);
        }
    }
    else if (args.Length > 0 && args[0] == "consume-all")
    {
        Console.WriteLine("___\nCONSUME-ALL");
        await foreach (var jsMsg in consumer.ConsumeAllAsync<RawData>(consumeOpts, cts.Token))
        {
            var msg = jsMsg.Msg;
            Console.WriteLine($"data: {msg.Data}");
            await jsMsg.AckAsync(cts.Token);
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
