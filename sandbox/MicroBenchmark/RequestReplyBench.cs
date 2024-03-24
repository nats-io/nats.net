using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class RequestReplyBench
{
    private readonly StreamConfig _streamConfig = new("EVENTS", new[] { "events.>" });

    private NatsConnection _nats;
    private NatsJSContext _js;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _nats = new NatsConnection();
        await _nats.ConnectAsync();
        _js = new NatsJSContext(_nats);
    }

    [Benchmark]
    public async Task<INatsJSStream> RequestAsync() =>
        await _js.CreateStreamAsync(_streamConfig);
}
