using System.Diagnostics;
using System.Text;
using Example.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

using var tracer = TracingSetup.RunSandboxTracing(internalTraces: false);

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var options = NatsOpts.Default with { LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()) };

await using var nats = new NatsConnection(options);

var js = new NatsJSContext(nats);

var consumer = await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig { Name = "c1", DurableName = "c1", AckPolicy = ConsumerConfigAckPolicy.Explicit });

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
    if (cmd == "fetch-no-wait")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                const int max = 10;
                Console.WriteLine($"___\nFETCH-NO-WAIT {max}");
                await consumer.RefreshAsync(cts.Token);

                var fetchNoWaitOpts = new NatsJSFetchOpts { MaxMsgs = max };
                var fetchMsgCount = 0;

                // NoWaitFetch is a specialized operation not available on the public interface.
                await foreach (var msg in ((NatsJSConsumer)consumer).FetchNoWaitAsync<NatsMemoryOwner<byte>>(opts: fetchNoWaitOpts, cancellationToken: cts.Token))
                {
                    using var activity = msg.StartChildActivity();
                    fetchMsgCount++;
                    using (msg.Data)
                    {
                        var message = Encoding.ASCII.GetString(msg.Data.Span);
                        Console.WriteLine($"Received: {message}");
                    }

                    await msg.AckAsync(cancellationToken: cts.Token);
                    Report(++count, stopwatch, $"data: {msg.Data}");
                }

                if (fetchMsgCount < fetchNoWaitOpts.MaxMsgs)
                {
                    Console.WriteLine("No more messages. Pause for more...");
                    await Task.Delay(TimeSpan.FromSeconds(5));
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
    else if (cmd == "fetch")
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"___\nFETCH {maxMsgs}");
                await consumer.RefreshAsync(cts.Token);
                await foreach (var msg in consumer.FetchAsync<NatsMemoryOwner<byte>>(opts: fetchOpts, cancellationToken: cts.Token))
                {
                    using var activity = msg.StartChildActivity();
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
                var next = await consumer.NextAsync<NatsMemoryOwner<byte>>(opts: nextOpts, cancellationToken: cts.Token);
                if (next is { } msg)
                {
                    using var activity = msg.StartChildActivity();
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
                var stopped = false;
                var consumeStop = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                await foreach (var msg in consumer.ConsumeAsync<NatsMemoryOwner<byte>>(opts: consumeOpts, cancellationToken: consumeStop.Token))
                {
                    using var activity = msg.StartChildActivity();
                    using (msg.Data)
                    {
                        var message = Encoding.ASCII.GetString(msg.Data.Span);
                        Console.WriteLine($"Received: {message}");
                        if (message == "stop")
                        {
                            Console.WriteLine("Stopping consumer...");
                            consumeStop.Cancel();
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
    else
    {
        Console.WriteLine("Usage: dotnet run -- <consume|consume-all|fetch|fetch-all|next>");
    }
}
catch (OperationCanceledException)
{
}

Console.WriteLine("Bye");
