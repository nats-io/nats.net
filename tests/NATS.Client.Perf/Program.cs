using System.Buffers;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.Core.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

var t = new TestParams
{
    Msgs = 1_000_000,
    Size = 128,
    Subject = "test",
    PubTasks = 10,
    MaxNatsBenchRatio = 0.20,
    MaxMemoryMb = 600,
    MaxAllocatedMb = 750,
};

Console.WriteLine("NATS NET v2 Perf Tests");
Console.WriteLine(t);

var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

await using var server = await NatsServerProcess.StartAsync();
await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

Console.WriteLine("\nRunning nats bench");
var natsBenchTotalMsgs = await RunNatsBenchAsync(server.Url, t, cts.Token);

await using var nats1 = new NatsConnection(new NatsOpts { Url = server.Url, SubPendingChannelFullMode = BoundedChannelFullMode.Wait });
await using var nats2 = new NatsConnection(new NatsOpts { Url = server.Url });

await nats1.PingAsync();
await nats2.PingAsync();

var subActive = 0;
var subReader = Task.Run(async () =>
{
    var count = 0;
    await using var sub = await nats1.SubscribeCoreAsync<NatsMemoryOwner<byte>>(t.Subject, cancellationToken: cts.Token);
    await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
    {
        using (msg.Data)
        {
            if (msg.Data.Length == 1)
            {
                Interlocked.Increment(ref subActive);
                continue;
            }

            if (++count == t.Msgs)
            {
                break;
            }
        }
    }
});

// Ensure subscription is active
while (Interlocked.CompareExchange(ref subActive, 0, 0) == 0)
{
    await Task.Delay(1000);
    await nats2.PublishAsync(t.Subject, 1, cancellationToken: cts.Token);
}

Console.WriteLine("# Sub synced");

var stopwatch = Stopwatch.StartNew();

var payload = new ReadOnlySequence<byte>(new byte[t.Size]);
var pubSync = 0;
var pubAsync = 0;
for (var i = 0; i < t.Msgs; i++)
{
    var vt = nats2.PublishAsync(t.Subject, payload, cancellationToken: cts.Token);
    if (vt.IsCompletedSuccessfully)
    {
        pubSync++;
        vt.GetAwaiter().GetResult();
    }
    else
    {
        pubAsync++;
        await vt;
    }
}

Console.WriteLine("pub time: {0}, sync: {1}, async: {2}", stopwatch.Elapsed, pubSync, pubAsync);
await subReader;
Console.WriteLine("sub time: {0}", stopwatch.Elapsed);

var seconds = stopwatch.Elapsed.TotalSeconds;

var meg = Math.Pow(2, 20);

var totalMsgs = 2.0 * t.Msgs / seconds;
var totalSizeMb = 2.0 * t.Msgs * t.Size / meg / seconds;

var memoryMb = Process.GetCurrentProcess().PrivateMemorySize64 / meg;

Console.WriteLine();
Console.WriteLine($"{totalMsgs:n0} msgs/sec ~ {totalSizeMb:n2} MB/sec");

var r = totalMsgs / natsBenchTotalMsgs;
Result.Add($"nats bench comparison: {r:n2} (> {t.MaxNatsBenchRatio})", () => r > t.MaxNatsBenchRatio);
Result.Add($"memory usage: {memoryMb:n2} MB (< {t.MaxMemoryMb} MB)", () => memoryMb < t.MaxMemoryMb);

var allocatedMb = GC.GetTotalAllocatedBytes() / meg;
Result.Add($"allocations: {allocatedMb:n2} MB (< {t.MaxAllocatedMb} MB)", () => allocatedMb < t.MaxAllocatedMb);

Console.WriteLine();
return Result.Eval();

async Task<double> RunNatsBenchAsync(string url, TestParams tp, CancellationToken ct)
{
    var sub = Task.Run(async () => await RunNatsBenchCmdAsync("sub", url, tp, ct));
    await RunNatsBenchCmdAsync("pub", url, tp with { Msgs = (int)(tp.Msgs * 1.2) }, ct); // +20% to cover potential loss
    var output = await sub;
    var match = Regex.Match(output, @"stats: (\S+) msgs/sec ~ (\S+) (\w+)/sec", RegexOptions.Multiline);
    var total = double.Parse(match.Groups[1].Value);

    Console.WriteLine(output);
    Console.WriteLine($"Parsed nats bench msgs {total:n0}");
    Console.WriteLine();

    return total;
}

async Task<string> RunNatsBenchCmdAsync(string cmd, string url, TestParams @params, CancellationToken ct)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "nats",
            Arguments = $"bench {cmd} {@params.Subject} --size={@params.Size} --msgs={@params.Msgs} --no-progress",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            Environment = { { "NATS_URL", $"{url}" } },
        },
    };
    process.Start();
    await process.WaitForExitAsync(ct);
    return await process.StandardOutput.ReadToEndAsync();
}

internal class Result
{
    private static readonly List<Result> Results = new();
    private readonly string _message;
    private readonly Func<bool> _test;

    private Result(string message, Func<bool> test)
    {
        _message = message;
        _test = test;
    }

    public static void Add(string message, Func<bool> test) =>
        Results.Add(new Result(message: message, test: test));

    public static int Eval()
    {
        var failed = 0;
        foreach (var result in Results)
        {
            var test = result._test();
            var ok = test ? "OK" : "NOT OK";
            Console.WriteLine($"[{ok}] {result._message}");
            if (test == false)
                failed++;
        }

        Console.WriteLine(failed == 0 ? "PASS" : "FAILED");

        return failed;
    }
}

internal record TestParams
{
    public int Msgs { get; init; }

    public string Subject { get; init; } = string.Empty;

    public int Size { get; init; }

    public int MaxMemoryMb { get; init; }

    public double MaxNatsBenchRatio { get; init; }

    public int PubTasks { get; init; }

    public int MaxAllocatedMb { get; init; }
}
