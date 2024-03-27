using BenchmarkDotNet.Attributes;
using NATS.Client;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using StreamInfo = NATS.Client.JetStream.StreamInfo;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class RequestReplyBench
{
    private readonly StreamConfig _streamConfig = new("benchreqrep", new[] { "benchreqrep.>" });
    private readonly StreamConfig _streamConfig2 = new("benchreqrepV2", new[] { "benchreqrepV2.>" })
    {
        NumReplicas = 1,
        Discard = StreamConfigDiscard.Old,
        DuplicateWindow = TimeSpan.Zero,
    };

    private readonly byte[] _data = new byte[128];

    private NatsConnection _nats;
    private NatsJSContext _js;
    private IJetStream _jetStream;
    private IJetStreamManagement _jetStreamManagement;
    private StreamConfiguration _streamConfiguration;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _nats = new NatsConnection();
        await _nats.ConnectAsync();
        _js = new NatsJSContext(_nats);

        // await CleanupAsync();

        await _js.CreateStreamAsync(_streamConfig);

        ConnectionFactory connectionFactory = new ConnectionFactory();
        Options opts = ConnectionFactory.GetDefaultOptions("localhost");
        var conn = connectionFactory.CreateConnection(opts);
        _jetStream = conn.CreateJetStreamContext();
        _jetStreamManagement = conn.CreateJetStreamManagementContext();
        _streamConfiguration = StreamConfiguration.Builder()
            .WithName("benchreqrepV1")
            .WithStorageType(StorageType.File)
            .WithSubjects("benchreqrepv1.>")
            .Build();
    }

    // [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await Task.Delay(1_000);
        await DeleteStreamAsync("benchreqrep");
        await DeleteStreamAsync("benchreqrepV1");
        await DeleteStreamAsync("benchreqrepV2");
        await Task.Delay(1_000);
    }

    [Benchmark]
    public async Task<PubAckResponse> JSPublishAsync() =>
        await _js.PublishAsync("benchreqrep.x", _data);

    [Benchmark]
    public async Task<PubAckResponse> JSPublishAsync2() =>
        await _js.PublishAsync2("benchreqrep.x", _data);
    //
    // [Benchmark]
    // public async Task<INatsJSStream> CreateStreamAsync() =>
    //     await _js.CreateStreamAsync(_streamConfig2);



    [Benchmark]
    public async Task<PublishAck> JSPublishAsyncV1() =>
        await _jetStream.PublishAsync(subject: "benchreqrep.x", data: _data);
    //
    // [Benchmark]
    // public StreamInfo? CreateStreamV1() =>
    //     _jetStreamManagement.AddStream(_streamConfiguration);
    //
    // [Benchmark]
    // public async Task<INatsJSStream> CreateStreamAsync2() =>
    //     await _js.CreateStreamAsync2(_streamConfig2);

    private async Task DeleteStreamAsync(string name)
    {
        try
        {
            await _js.DeleteStreamAsync(stream: name);
        }
        catch
        {
            // ignored
        }
    }
}
