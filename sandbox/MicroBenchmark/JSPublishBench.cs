using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class JSPublishBench
{
    private NatsConnection _nats;
    private NatsJSContext _js;

    [Params(1, 10, 1_000)]
    public int Batch { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _nats = new NatsConnection();
        _js = new NatsJSContext(_nats);
        await _nats.ConnectAsync();
        await _js.CreateStreamAsync(new StreamConfig("bench_test1", ["bench_test1"]));
        await _js.CreateStreamAsync(new StreamConfig("bench_test2", ["bench_test2"]));
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _js.DeleteStreamAsync("bench_test1");
        await _js.DeleteStreamAsync("bench_test2");
        await _nats.DisposeAsync();
    }

    [Benchmark]
    public async Task PublishAsync()
    {
        for (var i = 0; i < Batch; i++)
        {
            var ack = await _js.PublishAsync("bench_test1", i);
            ack.EnsureSuccess();
        }
    }

    [Benchmark]
    public async Task PublishConcurrentlyAsync()
    {
        var futures = new NatsJSPublishConcurrentFuture[Batch];

        for (var i = 0; i < Batch; i++)
        {
            futures[i] = await _js.PublishConcurrentAsync("bench_test2", i);
        }

        for (var i = 0; i < Batch; i++)
        {
            await using var future = futures[i];
            var ack = await future.GetResponseAsync();
            ack.EnsureSuccess();
        }
    }
}
