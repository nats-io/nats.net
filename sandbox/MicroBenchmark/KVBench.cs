using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

#pragma warning disable CS8618

namespace MicroBenchmark;

[MemoryDiagnoser]
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

    [Benchmark]
    public async ValueTask<int> TryGetAsync()
    {
        var result = await _store.TryGetEntryAsync<int>("does.not.exist");
        if (result is { Success: false, Error: NatsKVKeyNotFoundException })
        {
            return 1;
        }

        return 0;
    }

    [Benchmark]
    public async ValueTask<int> GetAsyncNew()
    {
        try
        {
            await _store.GetEntryAsyncNew<int>("does.not.exist");
        }
        catch (NatsKVKeyNotFoundException)
        {
            return 1;
        }

        return 0;
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<int> GetAsync()
    {
        try
        {
            await _store.GetEntryAsync<int>("does.not.exist");
        }
        catch (NatsKVKeyNotFoundException)
        {
            return 1;
        }

        return 0;
    }
}
