using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class SerializationBuffersBench
{
    private static readonly string Data = new('0', 126);
    private static readonly NatsPubOpts OptsWaitUntilSentTrue = new() { WaitUntilSent = true };
    private static readonly NatsPubOpts OptsWaitUntilSentFalse = new() { WaitUntilSent = false };

    private NatsConnection _nats;

    [Params(64, 512, 1024)]
    public int Iter { get; set; }

    [GlobalSetup]
    public void Setup() => _nats = new NatsConnection();

    [Benchmark]
    public async ValueTask<TimeSpan> WaitUntilSentTrue()
    {
        for (var i = 0; i < Iter; i++)
        {
            await _nats.PublishAsync("foo", Data, opts: OptsWaitUntilSentTrue);
        }

        return await _nats.PingAsync();
    }

    [Benchmark]
    public async ValueTask<TimeSpan> WaitUntilSentFalse()
    {
        for (var i = 0; i < Iter; i++)
        {
            await _nats.PublishAsync("foo", Data, opts: OptsWaitUntilSentFalse);
        }

        return await _nats.PingAsync();
    }
}
