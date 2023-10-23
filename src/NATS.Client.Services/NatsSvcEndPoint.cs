using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Client.Services;

public interface INatsSvcEndPoint : IAsyncDisposable
{
    long Requests { get; }

    long ProcessingTime { get; }

    long Errors { get; }

    string? LastError { get; }

    long AverageProcessingTime { get; }

    IDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// The subject name to subscribe to.
    /// </summary>
    string Subject { get; }

    /// <summary>
    /// If specified, the subscriber will join this queue group. Subscribers with the same queue group name,
    /// become a queue group, and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </summary>
    string? QueueGroup { get; }
}

public class NatsSvcEndPoint<T> : NatsSubBase, INatsSvcEndPoint
{
    private readonly ILogger _logger;
    private readonly Func<NatsSvcMsg<T>, ValueTask> _handler;
    private readonly NatsConnection _nats;
    private readonly string _name;
    private readonly CancellationToken _cancellationToken;
    private readonly Channel<NatsSvcMsg<T>> _channel;
    private readonly INatsSerializer _serializer;
    private readonly Task _handlerTask;

    private long _requests;
    private long _errors;
    private long _processingTime;
    private string? _lastError;

    public NatsSvcEndPoint(NatsConnection nats, string? queueGroup, string name, Func<NatsSvcMsg<T>, ValueTask> handler, string subject, IDictionary<string, string>? metadata, NatsSubOpts? opts, CancellationToken cancellationToken)
        : base(nats, nats.SubscriptionManager, subject, queueGroup, opts)
    {
        _logger = nats.Opts.LoggerFactory.CreateLogger<NatsSvcEndPoint<T>>();
        _handler = handler;
        _nats = nats;
        _name = name;
        Metadata = metadata;
        _cancellationToken = cancellationToken;
        _serializer = opts?.Serializer ?? _nats.Opts.Serializer;
        _channel = Channel.CreateBounded<NatsSvcMsg<T>>(128);
        _handlerTask = Task.Run(HandlerLoop);
    }

    public long Requests => Volatile.Read(ref _requests);

    public long ProcessingTime => Volatile.Read(ref _processingTime);

    public long Errors => Volatile.Read(ref _errors);

    public string? LastError => Volatile.Read(ref _lastError);

    public long AverageProcessingTime => Requests == 0 ? 0 : ProcessingTime / Requests;

    public IDictionary<string, string>? Metadata { get; }

    public override async ValueTask DisposeAsync()
    {
        await _handlerTask;
        await base.DisposeAsync();
    }

    internal ValueTask StartAsync(CancellationToken cancellationToken) =>
        _nats.SubAsync(this, cancellationToken);

    protected override ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        NatsMsg<T> msg;
        Exception? exception;
        try
        {
            msg = NatsMsg<T>.Build(subject, replyTo, headersBuffer, payloadBuffer, _nats, _nats.HeaderParser, _serializer);
            exception = null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Endpoint {Name} error building message", _name);
            exception = e;

            // Most likely a serialization error.
            // Make sure we have a valid message
            // so handler can reply with an error.
            msg = new NatsMsg<T>(subject, replyTo, subject.Length + (replyTo?.Length ?? 0), default, default, _nats);
        }

        return _channel.Writer.WriteAsync(new NatsSvcMsg<T>(msg, exception), _cancellationToken);
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
                int code;
                string message;
                string body;
                if (e is NatsSvcEndPointException epe)
                {
                    code = epe.Code;
                    message = epe.Message;
                    body = epe.Body;
                }
                else
                {
                    // Do not expose exceptions unless explicitly
                    // thrown as NatsSvcEndPointException
                    code = 999;
                    message = "Handler error";
                    body = string.Empty;

                    // Only log unknown exceptions
                    _logger.LogError(e, "Endpoint {Name} error processing message", _name);
                }

                Interlocked.Increment(ref _errors);
                Interlocked.Exchange(ref _lastError, message);

                try
                {
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        await svcMsg.ReplyErrorAsync(code, message, cancellationToken: _cancellationToken);
                    }
                    else
                    {
                        await svcMsg.ReplyErrorAsync(code, message, data: Encoding.UTF8.GetBytes(body), cancellationToken: _cancellationToken);
                    }
                }
                catch (Exception e1)
                {
                    _logger.LogError(e1, "Endpoint {Name} error responding", _name);
                }
            }
            finally
            {
                Interlocked.Add(ref _processingTime, ToNanos(stopwatch.Elapsed));
            }
        }
    }

    private long ToNanos(TimeSpan timeSpan) => (long)(timeSpan.TotalMilliseconds * 1_000_000);
}
