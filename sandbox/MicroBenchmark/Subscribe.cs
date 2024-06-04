using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using NATS.Client.Core;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class Subscribe
{
    private const int TotalMsgs = 500_000;
    private NatsConnection _nats;
    private CancellationTokenSource _cts;
    private Task _pubTask;

    [GlobalSetup]
    public async Task Setup()
    {
        _nats = new NatsConnection(NatsOpts.Default);
        await _nats.ConnectAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _nats.DisposeAsync();

    [IterationSetup]
    public void IterSetup()
    {
        _cts = new CancellationTokenSource();
        _pubTask = PubTask(_cts);
    }

    [IterationCleanup]
    public void IterCleanup()
    {
        _cts.Cancel();
        _pubTask.GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task SubscribeAsync()
    {
        var count = 0;
#pragma warning disable SA1312
        await foreach (var _ in _nats.SubscribeAsync<string>("test"))
#pragma warning restore SA1312
        {
            if (++count >= TotalMsgs)
            {
                return;
            }
        }
    }

    [Benchmark]
    public async Task CoreWait()
    {
        var count = 0;
        await using var sub = await _nats.SubscribeCoreAsync<string>("test");
        while (await sub.Msgs.WaitToReadAsync())
        {
            while (sub.Msgs.TryRead(out _))
            {
                if (++count >= TotalMsgs)
                {
                    return;
                }
            }
        }
    }

    [Benchmark]
    public async Task CoreRead()
    {
        var count = 0;
        await using var sub = await _nats.SubscribeCoreAsync<string>("test");
        while (true)
        {
            await sub.Msgs.ReadAsync();
            if (++count >= TotalMsgs)
            {
                return;
            }
        }
    }

    [Benchmark]
    public async Task CoreReadAll()
    {
        var count = 0;
        await using var sub = await _nats.SubscribeCoreAsync<string>("test");
#pragma warning disable SA1312
        await foreach (var _ in sub.Msgs.ReadAllAsync())
#pragma warning restore SA1312
        {
            if (++count >= TotalMsgs)
            {
                return;
            }
        }
    }

    // limit pub to the same rate across benchmarks
    // pub in batches so that groups of messages are available
    private Task PubTask(CancellationTokenSource cts) =>
        Task.Run(async () =>
        {
            const long pubMaxPerSecond = TotalMsgs;
            const long batchSize = 100;
            const long ticksBetweenBatches = TimeSpan.TicksPerSecond / pubMaxPerSecond * batchSize;

            var sw = new Stopwatch();
            sw.Start();
            var lastTick = sw.ElapsedTicks;
            var i = 0L;
            while (!cts.IsCancellationRequested)
            {
                await _nats.PublishAsync("test", "data");
                if (++i % batchSize == 0)
                {
                    while (sw.ElapsedTicks - lastTick < ticksBetweenBatches)
                    {
                    }

                    lastTick = sw.ElapsedTicks;
                }
            }
        });
}
