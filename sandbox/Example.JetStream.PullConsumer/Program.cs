using System.Buffers;
using System.Diagnostics;
using System.Text;
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

var idle = TimeSpan.FromSeconds(5);
var expires = TimeSpan.FromSeconds(10);

// int? maxMsgs = null;
// int? maxBytes = 128;
int? maxMsgs = 10;
int? maxBytes = null;

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
};

var fetchOpts = new NatsJSFetchOpts
{
    MaxMsgs = maxMsgs,
    MaxBytes = maxBytes,
    Expires = expires,
    IdleHeartbeat = idle,
};

var nextOpts = new NatsJSNextOpts
{
    Expires = expires,
    IdleHeartbeat = idle,
};

var stopwatch = Stopwatch.StartNew();
var count = 0;

var cmd = args.Length > 0 ? args[0] : "consume";
var cmdOpt = args.Length > 1 ? args[1] : "none";

try
{
    if (cmd == "fetch")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"___\nFETCH {maxMsgs}");
                await consumer.RefreshAsync(cts.Token);
                await using var sub = await consumer.FetchAsync<NatsMemoryOwner<byte>>(fetchOpts, cts.Token);
                await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
                {
                    using (msg.Data)
                    {
                        var message = Encoding.ASCII.GetString(msg.Data.Span);
                        Console.WriteLine($"Received: {message}");
                    }

                    await msg.AckAsync(cancellationToken: cts.Token);
                    Report(++count, stopwatch, $"data: {msg.Data}");
                }
            }
            catch (NatsJSProtocolException e)
            {
                Console.WriteLine(e.Message);
            }
            catch (NatsJSException e)
            {
                Console.WriteLine(e.Message);
                await Task.Delay(1000);
            }
        }
    }
    else if (cmd == "fetch-all")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"___\nFETCH {maxMsgs}");
                await consumer.RefreshAsync(cts.Token);
                await foreach (var msg in consumer.FetchAllAsync<NatsMemoryOwner<byte>>(fetchOpts, cts.Token))
                {
                    using (msg.Data)
                    {
                        var message = Encoding.ASCII.GetString(msg.Data.Span);
                        Console.WriteLine($"Received: {message}");
                    }

                    await msg.AckAsync(cancellationToken: cts.Token);
                    Report(++count, stopwatch, $"data: {msg.Data}");
                }
            }
            catch (NatsJSProtocolException e)
            {
                Console.WriteLine(e.Message);
            }
            catch (NatsJSException e)
            {
                Console.WriteLine(e.Message);
                await Task.Delay(1000);
            }
        }
    }
    else if (cmd == "next")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine("___\nNEXT");
                var next = await consumer.NextAsync<NatsMemoryOwner<byte>>(nextOpts, cts.Token);
                if (next is { } msg)
                {
                    using (msg.Data)
                    {
                        var message = Encoding.ASCII.GetString(msg.Data.Span);
                        Console.WriteLine($"Received: {message}");
                    }

                    await msg.AckAsync(cancellationToken: cts.Token);
                    Report(++count, stopwatch, $"data: {msg.Data}");
                }
            }
            catch (NatsJSProtocolException e)
            {
                Console.WriteLine(e.Message);
            }
            catch (NatsJSException e)
            {
                Console.WriteLine(e.Message);
                await Task.Delay(1000);
            }
        }
    }
    else if (cmd == "consume")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine("___\nCONSUME");
                await using var sub = await consumer.ConsumeAsync<NatsMemoryOwner<byte>>(consumeOpts);

                cts.Token.Register(() =>
                {
                    sub.DisposeAsync().GetAwaiter().GetResult();
                });

                var stopped = false;
                await foreach (var msg in sub.Msgs.ReadAllAsync())
                {
                    using (msg.Data)
                    {
                        var message = Encoding.ASCII.GetString(msg.Data.Span);
                        Console.WriteLine($"Received: {message}");
                        if (message == "stop")
                        {
                            Console.WriteLine("Stopping consumer...");
                            sub.Stop();
                            stopped = true;
                        }
                    }

                    await msg.AckAsync(cancellationToken: cts.Token);
                    Report(++count, stopwatch, $"data: {msg.Data}");

                    if (cmdOpt == "with-pause")
                    {
                        await Task.Delay(1_000);
                    }
                }

                if (stopped)
                {
                    Console.WriteLine("Stopped consumer.");
                    break;
                }
            }
            catch (NatsJSProtocolException e)
            {
                Console.WriteLine(e.Message);
            }
            catch (NatsJSException e)
            {
                Console.WriteLine(e.Message);
                await Task.Delay(1000);
            }
        }
    }
    else if (cmd == "consume-all")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine("___\nCONSUME-ALL");
                await foreach (var msg in consumer.ConsumeAllAsync<NatsMemoryOwner<byte>>(consumeOpts, cts.Token))
                {
                    using (msg.Data)
                    {
                        var message = Encoding.ASCII.GetString(msg.Data.Span);
                        Console.WriteLine($"Received: {message}");
                    }

                    await msg.AckAsync(cancellationToken: cts.Token);
                    Report(++count, stopwatch, $"data: {msg.Data}");
                }
            }
            catch (NatsJSProtocolException e)
            {
                Console.WriteLine(e.Message);
            }
            catch (NatsJSException e)
            {
                Console.WriteLine(e.Message);
                await Task.Delay(1000);
            }
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
