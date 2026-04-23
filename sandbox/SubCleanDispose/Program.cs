// Manual dispose-race repro for NATS subscriptions and JetStream consumers.
//
// Each scenario publishes N messages with sequential payloads, starts a
// subscriber/consumer, cancels mid-flight, then checks the received set for
// gaps. A gap (received {1, 2, 4} with no 3) means a message was dropped
// during dispose.
//
// Usage: SubCleanDispose [iterations]
//   NATS_URL env var overrides the server URL (default nats://127.0.0.1:4222).
using System.Diagnostics;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

var url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://127.0.0.1:4222";
var iterations = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 20;

Console.WriteLine($"url={url} iterations={iterations}");
Console.WriteLine();

var failures = new Dictionary<string, int>
{
    ["core-sub"] = 0,
    ["js-fetch"] = 0,
    ["js-consume"] = 0,
    ["js-ordered"] = 0,
};

for (var i = 1; i <= iterations; i++)
{
    await RunOnce("core-sub", i, () => TestCoreSub(url));
    await RunOnce("js-fetch", i, () => TestJsFetch(url));
    await RunOnce("js-consume", i, () => TestJsConsume(url));
    await RunOnce("js-ordered", i, () => TestJsOrdered(url));
}

Console.WriteLine();
Console.WriteLine("Summary:");
foreach (var (name, count) in failures)
    Console.WriteLine($"  {name,-12} {count} failure(s)");

return failures.Values.Sum() == 0 ? 0 : 1;

async Task RunOnce(string name, int iter, Func<Task<Result>> body)
{
    var sw = Stopwatch.StartNew();
    Result r;
    try
    {
        r = await body();
    }
    catch (Exception e)
    {
        Console.WriteLine($"[{name}] iter {iter} ERROR {e.GetType().Name}: {e.Message}");
        failures[name]++;
        return;
    }

    sw.Stop();
    var ok = r.Gap == 0;
    if (!ok)
        failures[name]++;
    Console.WriteLine(
        $"[{name}] iter {iter,3} published={r.Published,5} received={r.Received,5} highest={r.Highest,5} gap={r.Gap,4} {sw.ElapsedMilliseconds,5}ms {(ok ? "ok" : "DROP")}");
}

static async Task<Result> TestCoreSub(string url)
{
    const int published = 1000;
    await using var nats = new NatsConnection(new NatsOpts { Url = url });
    await nats.ConnectAsync();

    var subject = $"t.{Guid.NewGuid():N}";
    var received = new HashSet<int>();

    await using var sub = await nats.SubscribeCoreAsync<int>(subject);
    await nats.PingAsync();

    var readerDone = new TaskCompletionSource();
    var reader = Task.Run(async () =>
    {
        try
        {
            await foreach (var msg in sub.Msgs.ReadAllAsync())
            {
                received.Add(msg.Data);
                if (received.Count == 10)
                    readerDone.TrySetResult();
            }
        }
        catch (Exception)
        {
        }
    });

    for (var i = 1; i <= published; i++)
        await nats.PublishAsync(subject, i);

    await readerDone.Task;

    // Dispose while messages may still be in-flight. Core NATS has no
    // server-side ack tracking, so gaps here indicate drop-on-dispose.
    await sub.DisposeAsync();
    await reader;

    return Result.From(published, received);
}

static async Task<Result> TestJsFetch(string url)
{
    const int published = 1000;
    await using var nats = new NatsConnection(new NatsOpts { Url = url });
    var js = new NatsJSContext(nats);
    var id = Guid.NewGuid().ToString("N").Substring(0, 8);
    var stream = $"s_{id}";
    var subject = $"s_{id}.x";
    var consumer = $"c_{id}";

    await js.CreateStreamAsync(new StreamConfig(stream, new[] { subject }));
    var c = await js.CreateOrUpdateConsumerAsync(stream, new ConsumerConfig(consumer) { AckPolicy = ConsumerConfigAckPolicy.Explicit });

    for (var i = 1; i <= published; i++)
        await js.PublishAsync(subject, i);

    var received = new HashSet<int>();
    using var cts = new CancellationTokenSource();
    try
    {
        await foreach (var m in c.FetchAsync<int>(new NatsJSFetchOpts { MaxMsgs = published, Expires = TimeSpan.FromSeconds(10) }, cancellationToken: cts.Token))
        {
            received.Add(m.Data);
            await m.AckAsync();
            if (received.Count == 10)
                cts.CancelAfter(TimeSpan.FromMilliseconds(1));
        }
    }
    catch (OperationCanceledException)
    {
    }

    await js.DeleteStreamAsync(stream);
    return Result.From(published, received);
}

static async Task<Result> TestJsConsume(string url)
{
    const int published = 1000;
    await using var nats = new NatsConnection(new NatsOpts { Url = url });
    var js = new NatsJSContext(nats);
    var id = Guid.NewGuid().ToString("N").Substring(0, 8);
    var stream = $"s_{id}";
    var subject = $"s_{id}.x";
    var consumer = $"c_{id}";

    await js.CreateStreamAsync(new StreamConfig(stream, new[] { subject }));
    var c = await js.CreateOrUpdateConsumerAsync(stream, new ConsumerConfig(consumer) { AckPolicy = ConsumerConfigAckPolicy.Explicit });

    for (var i = 1; i <= published; i++)
        await js.PublishAsync(subject, i);

    var received = new HashSet<int>();
    using var cts = new CancellationTokenSource();
    try
    {
        await foreach (var m in c.ConsumeAsync<int>(opts: new NatsJSConsumeOpts { MaxMsgs = published }, cancellationToken: cts.Token))
        {
            received.Add(m.Data);
            await m.AckAsync();
            if (received.Count == 10)
                cts.CancelAfter(TimeSpan.FromMilliseconds(1));
        }
    }
    catch (OperationCanceledException)
    {
    }

    await js.DeleteStreamAsync(stream);
    return Result.From(published, received);
}

static async Task<Result> TestJsOrdered(string url)
{
    const int published = 1000;
    await using var nats = new NatsConnection(new NatsOpts { Url = url });
    var js = new NatsJSContext(nats);
    var id = Guid.NewGuid().ToString("N").Substring(0, 8);
    var stream = $"s_{id}";
    var subject = $"s_{id}.x";

    await js.CreateStreamAsync(new StreamConfig(stream, new[] { subject }));
    var c = await js.CreateOrderedConsumerAsync(stream, new NatsJSOrderedConsumerOpts { FilterSubjects = new[] { subject } });

    for (var i = 1; i <= published; i++)
        await js.PublishAsync(subject, i);

    var received = new HashSet<int>();
    using var cts = new CancellationTokenSource();
    try
    {
        await foreach (var m in c.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            received.Add(m.Data);
            if (received.Count == 10)
                cts.CancelAfter(TimeSpan.FromMilliseconds(1));
        }
    }
    catch (OperationCanceledException)
    {
    }

    await js.DeleteStreamAsync(stream);
    return Result.From(published, received);
}

internal readonly record struct Result(int Published, int Received, int Highest, int Gap)
{
    public static Result From(int published, HashSet<int> received)
    {
        if (received.Count == 0)
            return new Result(published, 0, 0, 0);
        var max = received.Max();

        // Gap = "how many integers in [1..max] are missing from received".
        var gap = max - received.Count;
        return new Result(published, received.Count, max, gap);
    }
}
