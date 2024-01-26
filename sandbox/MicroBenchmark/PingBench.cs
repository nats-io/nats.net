using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class PingBench
{
    private NatsConnection _nats;

    [Params(64, 512, 1024)]
    public int Iter { get; set; }

    [GlobalSetup]
    public void Setup() => _nats = new NatsConnection();

    [Benchmark]
    public async ValueTask<TimeSpan> PingAsync()
    {
        var total = TimeSpan.Zero;

        for (var i = 0; i < Iter; i++)
        {
            total += await _nats.PingAsync();
        }

        return total;
    }
}
