using BenchmarkDotNet.Attributes;
using NATS.Client.Core;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class RequestReplyBench
{
    private NatsConnection _nats1;
    private NatsConnection _nats2;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _nats1 = new NatsConnection();
        _nats2 = new NatsConnection(new NatsOpts { RequestReplyMode = NatsRequestReplyMode.Direct });
        await _nats1.ConnectAsync();
        await _nats2.ConnectAsync();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _nats1.DisposeAsync();
        await _nats2.DisposeAsync();
    }

    [Benchmark(Baseline = true)]
    public async Task<string> RequestReplyAsync() => await GetResultAsync(_nats1);

    [Benchmark]
    public async Task<string> RequestReplyDirectAsync() => await GetResultAsync(_nats2);

    private static async Task<string> GetResultAsync(NatsConnection nats)
    {
        var reply = await nats.RequestAsync<string>("$JS.API.INFO");
        var result = reply.Data;
        ArgumentException.ThrowIfNullOrEmpty(result);
        return result;
    }
}
