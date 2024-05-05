using BenchmarkDotNet.Attributes;
using NATS.Client.Core;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class RequestReplyBench
{
    private NatsConnection _nats;
    private CancellationTokenSource _cts;
    private Task _subscription;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _nats = new NatsConnection();
        await _nats.ConnectAsync();
        _cts = new CancellationTokenSource();
        _subscription = Task.Run(async () =>
        {
            await foreach (var msg in _nats.SubscribeAsync<int>("req_rep_bench", cancellationToken: _cts.Token))
            {
                await msg.ReplyAsync(0xBEEF);
            }
        });
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _cts.CancelAsync();
        await _subscription;
        await _nats.DisposeAsync();
    }

    [Benchmark]
    public async Task<int> RequestReplyAsync()
    {
        var reply = await _nats.RequestAsync<int, int>("req_rep_bench", 0xDEAD);
        var result = reply.Data;
        ArgumentOutOfRangeException.ThrowIfNotEqual(0xBEEF, result);
        return result;
    }
}
