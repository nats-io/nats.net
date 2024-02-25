using BenchmarkDotNet.Attributes;
using NATS.Client.Core.NaCl;

namespace MicroBenchmark;

[ShortRunJob]
[MemoryDiagnoser]
[PlainExporter]
public class Sha512TBench
{
    private byte[] _data = default!;

    [Params(5000)]
    public int Iter { get; set; }

    [Params(1024, 1024 * 25)]
    public int DataSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(4711);
        _data = new byte[DataSize];
        random.NextBytes(_data);
    }

    [Benchmark]
    public int Sha512Hash()
    {
        var result = 0;
        for (var i = 0; i < Iter; i++)
        {
            var hash = Sha512.Hash(_data, 0, _data.Length)!;
            result += hash.Length;
        }

        return result;
    }
}
