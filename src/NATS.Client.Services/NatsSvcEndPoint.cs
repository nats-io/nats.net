using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Client.Services;

public class NatsSvcEndPoint : NatsSubBase
{
    private readonly ILogger _logger;
    private readonly Func<NatsSvcMsg, ValueTask> _handler;
    private readonly NatsConnection _nats;
    private readonly NatsSvcConfig _config;
    private readonly string _name;
    private readonly string? _subject;
    private readonly NatsSubOpts? _opts;
    private readonly CancellationToken _cancellationToken;
    private readonly Channel<NatsSvcMsg> _channel;
    private readonly Task _handlerTask;

    private long _requests;
    private long _errors;
    private long _processingTime;
    private string? _lastError;

    public NatsSvcEndPoint(NatsConnection nats, NatsSvcConfig config, string name, Func<NatsSvcMsg, ValueTask> handler, string? subject, IDictionary<string, string>? metadata, NatsSubOpts? opts, CancellationToken cancellationToken)
        : base(nats, nats.SubscriptionManager, subject ?? name, config.QueueGroup, opts)
    {
        _logger = nats.Opts.LoggerFactory.CreateLogger<NatsSvcEndPoint>();
        _handler = handler;
        _nats = nats;
        _config = config;
        _name = name;
        _subject = subject;
        Metadata = metadata;
        _opts = opts;
        _cancellationToken = cancellationToken;
        _channel = Channel.CreateBounded<NatsSvcMsg>(128);
        _handlerTask = Task.Run(HandlerLoop);
    }

    public long Requests => Volatile.Read(ref _requests);

    public long ProcessingTime => Volatile.Read(ref _processingTime);

    public long Errors => Volatile.Read(ref _errors);

    public string? LastError => Volatile.Read(ref _lastError);

    public long AverageProcessingTime => Requests == 0 ? 0 : ProcessingTime / Requests;

    public ChannelReader<NatsSvcMsg> Msgs => _channel.Reader;

    public IDictionary<string, string>? Metadata { get; }

    internal ValueTask StartAsync(CancellationToken cancellationToken) =>
        _nats.SubAsync(this, cancellationToken);

    protected override ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        var msg = NatsMsg<NatsMemoryOwner<byte>>.Build(subject, replyTo, headersBuffer, payloadBuffer, _nats, _nats.HeaderParser, _nats.Opts.Serializer);
        return _channel.Writer.WriteAsync(new NatsSvcMsg(msg), _cancellationToken);
    }

    protected override void TryComplete() => _channel.Writer.TryComplete();

    private async Task HandlerLoop()
    {
        var stopwatch = new Stopwatch();
        await foreach (var svcMsg in _channel.Reader.ReadAllAsync(_cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Increment(ref _requests);
            stopwatch.Restart();
            try
            {
                await _handler(svcMsg).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Endpoint ({Name}/{Subject}) error processing message", _name, _subject);
                Interlocked.Increment(ref _errors);
                Interlocked.Exchange(ref _lastError, e.GetBaseException().Message);
            }
            finally
            {
                Interlocked.Add(ref _processingTime, ToNanos(stopwatch.Elapsed));
            }
        }
    }

    private long ToNanos(TimeSpan timeSpan) => (long)(timeSpan.TotalMilliseconds * 1_000_000);
}
