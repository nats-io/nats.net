using BenchmarkDotNet.Attributes;
using NATS.NKeys;

namespace MicroBenchmark;

[ShortRunJob]
[MemoryDiagnoser]
[PlainExporter]
public class NKeyBench
{
    [Params(5000)]
    public int Iter { get; set; }

    [Benchmark]
    public int NKeyCreate()
    {
        var result = 0;
        for (var i = 0; i < Iter; i++)
        {
            using var kp = KeyPair.FromSeed("SUAAVWRZG6M5FA5VRRGWSCIHKTOJC7EWNIT4JV3FTOIPO4OBFR5WA7X5TE".AsSpan());
            result += kp.GetPublicKey().Length;
        }

        return result;
    }
}
