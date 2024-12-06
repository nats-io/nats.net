using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

#pragma warning disable CS8618

namespace MicroBenchmark;

[MemoryDiagnoser]
// [ShortRunJob]
[PlainExporter]
public class KVBench
{
    private NatsConnection _nats;
    private NatsJSContext _js;
    private NatsKVContext _kv;
    private NatsKVStore _store;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _nats = new NatsConnection();
        _js = new NatsJSContext(_nats);
        _kv = new NatsKVContext(_js);
        _store = (NatsKVStore)(await _kv.CreateStoreAsync("benchmark"));
    }

    // [Benchmark(Baseline = true)]
    // public async ValueTask<int> TryGetAsync()
    // {
    //     var result = await _store.TryGetEntryAsync<int>("does.not.exist");
    //     if (result is { Success: false, Error: NatsKVKeyNotFoundException })
    //     {
    //         return 1;
    //     }
    //
    //     return 0;
    // }
    //
    // [Benchmark]
    // public async ValueTask<int> TryGetAsync2()
    // {
    //     var result = await _store.TryGetEntryAsync2<int>("does.not.exist");
    //     if (result is { Success: false, Error: NatsKVKeyNotFoundException })
    //     {
    //         return 1;
    //     }
    //
    //     return 0;
    // }
    //
    // [Benchmark]
    // public async ValueTask<int> TryGetAsync3()
    // {
    //     var result = await _store.TryGetEntryAsync3<int>("does.not.exist");
    //     if (result is { Success: false, Error: NatsKVKeyNotFoundException })
    //     {
    //         return 1;
    //     }
    //
    //     return 0;
    // }

    [Benchmark(Baseline = true)]
    public string StringOrig() => _store.StringOrig("does.not.exist");

    [Benchmark]
    public string StringInter() => _store.StringInter("does.not.exist");

    [Benchmark]
    public string StringConcat() => _store.StringConcat("does.not.exist");

    [Benchmark]
    public string StringCreate() => _store.StringCreate("does.not.exist");

    //
    // [Benchmark(Baseline = true)]
    // public async ValueTask<int> GetAsync()
    // {
    //     try
    //     {
    //         await _store.GetEntryAsync<int>("does.not.exist");
    //     }
    //     catch (NatsKVKeyNotFoundException)
    //     {
    //         return 1;
    //     }
    //
    //     return 0;
    // }
    //
    // [Benchmark]
    // public async ValueTask<int> TryGetMultiAsync()
    // {
    //     List<Task> tasks = new();
    //     for (var i = 0; i < 100; i++)
    //     {
    //         tasks.Add(Task.Run(async () =>
    //         {
    //             var result = await _store.TryGetEntryAsync<int>("does.not.exist");
    //             if (result is { Success: false, Error: NatsKVKeyNotFoundException })
    //             {
    //                 return 1;
    //             }
    //
    //             return 0;
    //         }));
    //     }
    //
    //     await Task.WhenAll(tasks);
    //
    //     return 0;
    // }
    //
    // [Benchmark]
    // public async ValueTask<int> GetMultiAsync()
    // {
    //     List<Task> tasks = new();
    //     for (var i = 0; i < 100; i++)
    //     {
    //         tasks.Add(Task.Run(async () =>
    //         {
    //             try
    //             {
    //                 await _store.GetEntryAsync<int>("does.not.exist");
    //             }
    //             catch (NatsKVKeyNotFoundException)
    //             {
    //                 return 1;
    //             }
    //
    //             return 0;
    //         }));
    //     }
    //
    //     await Task.WhenAll(tasks);
    //
    //     return 0;
    // }
}
