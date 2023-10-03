using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

/*
| Method                  | Iter | Mean        | Error        | StdDev      | Gen0     | Gen1    | Gen2    | Allocated |
|------------------------ |----- |------------:|-------------:|------------:|---------:|--------:|--------:|----------:|
| WaitUntilSentTrue       | 64   |  2,828.9 us |  1,637.15 us |    89.74 us |        - |       - |       - |    6996 B |
| WaitUntilSentFalse      | 64   |    161.5 us |     39.35 us |     2.16 us |        - |       - |       - |     602 B |
| WaitUntilSentFalseEarly | 64   |    216.9 us |     74.63 us |     4.09 us |  14.4043 |  7.0801 |       - |  200043 B |
| WaitUntilSentTrue       | 1000 | 43,930.1 us | 48,173.78 us | 2,640.57 us |        - |       - |       - |  105673 B |
| WaitUntilSentFalse      | 1000 |    723.5 us |    113.32 us |     6.21 us |   2.9297 |       - |       - |   48479 B |
| WaitUntilSentFalseEarly | 1000 |  1,136.7 us |    175.79 us |     9.64 us | 183.5938 | 91.7969 | 35.1563 | 2507530 B |
 */
[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class SerializationBuffersBench
{
    private static readonly string Data = new('0', 126);
    private static readonly NatsPubOpts OptsWaitUntilSentTrue = new() { WaitUntilSent = true };
    private static readonly NatsPubOpts OptsWaitUntilSentFalse = new() { WaitUntilSent = false };
    private static readonly NatsPubOpts OptsWaitUntilSentFalseEarly = new() { WaitUntilSent = false, SerializeEarly = true };

    private NatsConnection _nats;

    [Params(64, 1_000)]
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

    [Benchmark]
    public async ValueTask<TimeSpan> WaitUntilSentFalseEarly()
    {
        for (var i = 0; i < Iter; i++)
        {
            await _nats.PublishAsync("foo", Data, opts: OptsWaitUntilSentFalseEarly);
        }

        return await _nats.PingAsync();
    }
}
