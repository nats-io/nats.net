using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NATS.Client.ObjectStore.Internal;

namespace NATS.Client.ObjectStore.Encoder.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net60)]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net10_0)]
#if WINDOWS
[SimpleJob(RuntimeMoniker.Net481)]
#endif
public class Base64UrlEncoderBenchmarks
{
    private byte[] _data = null!;

    // SHA-256 digest (32) is the common case; the others bracket small and larger inputs.
    [Params(16, 32, 256, 4096)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Size];
        for (var i = 0; i < Size; i++)
            _data[i] = (byte)((i * 37) + 11);
    }

    [Benchmark]
    public string Encode() => Base64UrlEncoder.Encode(_data);
}
