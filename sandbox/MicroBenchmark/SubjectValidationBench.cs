using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NATS.Client.Core;

namespace MicroBenchmark;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net481)]
[SimpleJob(RuntimeMoniker.Net80)]
public class SubjectValidationBench
{
    private const int MessageCount = 1024;
    private const string ShortSubject = "foo.bar.baz";
    private const string LongSubject = "org.company.division.team.project.service.module.component.action.event.type.version.region";
    private const string ShortReplyTo = "reply.to.subject";
    private const string LongReplyTo = "org.company.division.team.project.service.module.component.reply.inbox.unique.identifier";
    private static readonly string Data = new('0', 128);

    private NatsConnection _natsValidation = null!;
    private NatsConnection _natsNoValidation = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _natsValidation = new NatsConnection(NatsOpts.Default with { SkipSubjectValidation = false });
        await _natsValidation.ConnectAsync();

        _natsNoValidation = new NatsConnection(NatsOpts.Default with { SkipSubjectValidation = true });
        await _natsNoValidation.ConnectAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _natsValidation.DisposeAsync();
        await _natsNoValidation.DisposeAsync();
    }

    [Benchmark(Baseline = true)]
    public async Task PublishAsync_Short_NoValidation()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            await _natsNoValidation.PublishAsync(ShortSubject, Data, replyTo: ShortReplyTo);
        }

        await _natsNoValidation.PingAsync();
    }

    [Benchmark]
    public async Task PublishAsync_Short_WithValidation()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            await _natsValidation.PublishAsync(ShortSubject, Data, replyTo: ShortReplyTo);
        }

        await _natsValidation.PingAsync();
    }

    [Benchmark]
    public async Task PublishAsync_Long_NoValidation()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            await _natsNoValidation.PublishAsync(LongSubject, Data, replyTo: LongReplyTo);
        }

        await _natsNoValidation.PingAsync();
    }

    [Benchmark]
    public async Task PublishAsync_Long_WithValidation()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            await _natsValidation.PublishAsync(LongSubject, Data, replyTo: LongReplyTo);
        }

        await _natsValidation.PingAsync();
    }
}
