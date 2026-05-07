using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NATS.Client.Core;

#pragma warning disable CS8618

namespace MicroBenchmark;

// Tight serial publish loop across runtimes.
// Measures end-to-end throughput including buffering, pipe writes, and socket I/O.
// The Lock change affects CommandWriter._lock on each publish, but the body under
// the lock dominates, so any visible delta here reflects runtime improvements broadly.
// Requires a running NATS server on localhost:4222.
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
public class PublishRuntimeBench
{
    private const int Msgs = 100_000;
    private static readonly byte[] Payload = new byte[128];

    private NatsConnection _nats;

    [GlobalSetup]
    public async Task Setup()
    {
        _nats = new NatsConnection();
        await _nats.ConnectAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _nats.DisposeAsync();

    [Benchmark]
    public async Task Publish100k()
    {
        for (var i = 0; i < Msgs; i++)
        {
            await _nats.PublishAsync("bench", Payload);
        }

        await _nats.PingAsync();
    }
}
