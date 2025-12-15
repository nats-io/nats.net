using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NATS.Client.Core;
using NATS.Client.Core.Commands;

namespace MicroBenchmark;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net481)]
[SimpleJob(RuntimeMoniker.Net80)]
public class SubjectValidationBench
{
    private const int MessageCount = 1024;
    private const string Subject = "foo.bar.baz";
    private const string ReplyTo = "reply.to.subject";
    private static readonly string Data = new('0', 128);

    private ProtocolWriter _protocolWriter = null!;
    private NatsBufferWriter<byte> _bufferWriter = null!;
    private ReadOnlyMemory<byte> _payload;
    private NatsConnection _nats = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _protocolWriter = new ProtocolWriter(Encoding.UTF8);
        _bufferWriter = new NatsBufferWriter<byte>(1024 * 1024);
        _payload = new byte[128];
        _nats = new NatsConnection();
        await _nats.ConnectAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _bufferWriter.Dispose();
        await _nats.DisposeAsync();
    }

    [Benchmark]
    public void WritePublish_NoReplyTo()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            _protocolWriter.WritePublish(_bufferWriter, Subject, replyTo: null, headers: null, _payload);
        }

        _bufferWriter.Clear();
    }

    [Benchmark]
    public void WritePublish_WithReplyTo()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            _protocolWriter.WritePublish(_bufferWriter, Subject, ReplyTo, headers: null, _payload);
        }

        _bufferWriter.Clear();
    }

    [Benchmark]
    public async Task PublishAsync_NoReplyTo()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            await _nats.PublishAsync(Subject, Data);
        }

        await _nats.PingAsync();
    }

    [Benchmark]
    public async Task PublishAsync_WithReplyTo()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            await _nats.PublishAsync(Subject, Data, replyTo: ReplyTo);
        }

        await _nats.PingAsync();
    }
}
