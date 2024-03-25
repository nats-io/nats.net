using BenchmarkDotNet.Attributes;
using NATS.Client;
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
    private readonly StreamConfig _streamConfig = new("benchreqrep", new[] { "benchreqrep.>" });
    private readonly byte[] _data = new byte[128];

    private NatsConnection _nats;
    private NatsJSContext _js;
    private IJetStream _jetStream;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _nats = new NatsConnection();
        await _nats.ConnectAsync();
        _js = new NatsJSContext(_nats);

        await Task.Delay(1_000);
        try
        {
            await _js.DeleteStreamAsync("benchreqrep");
        }
        catch
        {
            // ignored
        }

        await Task.Delay(1_000);
        await _js.CreateStreamAsync(_streamConfig);

        ConnectionFactory connectionFactory = new ConnectionFactory();
        Options opts = ConnectionFactory.GetDefaultOptions("localhost");
        var conn = connectionFactory.CreateConnection(opts);
        _jetStream = conn.CreateJetStreamContext();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await Task.Delay(1_000);
        try
        {
            await _js.DeleteStreamAsync("benchreqrep");
        }
        catch
        {
            // ignored
        }

        await Task.Delay(1_000);
    }

    [Benchmark]
    public async Task<PubAckResponse> RequestAsync() =>
        await _js.PublishAsync("benchreqrep.x", _data);

    [Benchmark]
    public async Task<PubAckResponse> RequestAsync2() =>
        await _js.PublishAsync2("benchreqrep.x", _data);

    [Benchmark]
    public async Task<PublishAck> RequestAsyncV1() =>
        await _jetStream.PublishAsync(subject: "benchreqrep.x", data: _data);
}
