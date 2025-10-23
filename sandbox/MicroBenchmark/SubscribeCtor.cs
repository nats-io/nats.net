using BenchmarkDotNet.Attributes;
using NATS.Client.Core;

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class SubscribeCtor
{
    private readonly NatsConnection _natsConnection = new();
    private readonly NatsSubOpts _opts = new()
    {
        Timeout = TimeSpan.FromSeconds(1),
        StartUpTimeout = TimeSpan.FromSeconds(1),
        IdleTimeout = TimeSpan.FromSeconds(1),
    };

    [Benchmark]
    public NatsSub<int> NewNatsSub()
        => new(_natsConnection, null, null, null, _opts, null);
}
