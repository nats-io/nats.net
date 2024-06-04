using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class PublishSerialBench
{
    private static readonly string Data = new('0', 126);

    private NatsConnection _nats;

    [Params(64, 512, 1024)]
    public int Iter { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _nats = new NatsConnection();
        await _nats.ConnectAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _nats.DisposeAsync();

    [Benchmark]
    public async Task PublishAsync()
    {
        for (var i = 0; i < Iter; i++)
        {
            await _nats.PublishAsync("foo", Data);
        }

        await _nats.PingAsync();
    }
}
