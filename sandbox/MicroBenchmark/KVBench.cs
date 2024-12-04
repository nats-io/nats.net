using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

#pragma warning disable CS8618

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class KVBench
{
    private NatsConnection _nats;
    private NatsJSContext _js;
    private NatsKVContext _kv;
    private NatsKVStore _store;

    [Params(64, 512, 1024)]
    public int Iter { get; set; }

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
        var total = 0;
        for (var i = 0; i < Iter; i++)
        {
            var result = await _store.TryGetEntryAsync<int>("does.not.exist");
            if (result is { Success: false, Error: NatsKVKeyNotFoundException })
                total++;
        }

        if (total != Iter)
            throw new Exception();

        return total;
    }

    [Benchmark]
    public async ValueTask<int> GetAsyncNew()
    {
        var total = 0;
        for (var i = 0; i < Iter; i++)
        {
            try
            {
                await _store.GetEntryAsyncNew<int>("does.not.exist");
            }
            catch (NatsKVKeyNotFoundException)
            {
                total++;
            }
        }

        if (total != Iter)
            throw new Exception();

        return total;
    }

    [Benchmark]
    public async ValueTask<int> GetAsync()
    {
        var total = 0;
        for (var i = 0; i < Iter; i++)
        {
            try
            {
                await _store.GetEntryAsync<int>("does.not.exist");
            }
            catch (NatsKVKeyNotFoundException)
            {
                total++;
            }
        }

        if (total != Iter)
            throw new Exception();

        return total;
    }
}
