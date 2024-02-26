using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;

namespace NATS.Client.Services;

/// <summary>
/// NATS service endpoint.
/// </summary>
public interface INatsSvcEndpoint : IAsyncDisposable
{
    /// <summary>
    /// Number of requests received.
    /// </summary>
    long Requests { get; }

    /// <summary>
    /// Total processing time in nanoseconds.
    /// </summary>
    long ProcessingTime { get; }

    /// <summary>
    /// Number of errors.
    /// </summary>
    long Errors { get; }

    /// <summary>
    /// Last error message.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Average processing time in nanoseconds.
    /// </summary>
    long AverageProcessingTime { get; }

    /// <summary>
    /// Endpoint metadata.
    /// </summary>
    IDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// The name of the endpoint.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The subject name to subscribe to.
    /// </summary>
    string Subject { get; }

    /// <summary>
    /// Endpoint queue group.
    /// </summary>
    /// <remarks>
    /// If specified, the subscriber will join this queue group. Subscribers with the same queue group name,
    /// become a queue group, and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </remarks>
    string? QueueGroup { get; }
}

/// <summary>
/// Endpoint base class exposing general stats.
/// </summary>
public abstract class NatsSvcEndpointBase : NatsSubBase, INatsSvcEndpoint
{
    protected NatsSvcEndpointBase(NatsConnection connection, string subject, string? queueGroup, NatsSubOpts? opts)
        : base(connection, connection.SubscriptionManager, subject, queueGroup, opts)
    {
    }

    /// <inheritdoc/>
    public abstract long Requests { get; }

    /// <inheritdoc/>
    public abstract long ProcessingTime { get; }

    /// <inheritdoc/>
    public abstract long Errors { get; }

    /// <inheritdoc/>
    public abstract string? LastError { get; }

    /// <inheritdoc/>
    public abstract long AverageProcessingTime { get; }

    /// <inheritdoc/>
    public abstract IDictionary<string, string>? Metadata { get; }

    /// <inheritdoc/>
    public abstract string Name { get; }

    internal abstract void IncrementErrors();

    internal abstract void SetLastError(string error);
}

/// <summary>
/// NATS service endpoint.
/// </summary>
/// <typeparam name="T">Serialized type to use when receiving data.</typeparam>
public class NatsSvcEndpoint<T> : NatsSvcEndpointBase
{
    private readonly ILogger _logger;
    private readonly Func<NatsSvcMsg<T>, ValueTask> _handler;
    private readonly NatsConnection _nats;
    private readonly CancellationToken _cancellationToken;
    private readonly Channel<NatsSvcMsg<T>> _channel;
    private readonly INatsDeserialize<T> _serializer;
    private readonly Task _handlerTask;

    private long _requests;
    private long _errors;
    private long _processingTime;
    private string? _lastError;

    /// <summary>
    /// Creates a new instance of <see cref="NatsSvcEndpoint{T}"/>.
    /// </summary>
    /// <param name="nats">NATS connection.</param>
    /// <param name="queueGroup">Queue group.</param>
    /// <param name="name">Optional endpoint name.</param>
    /// <param name="handler">Callback function to handle messages received.</param>
    /// <param name="subject">Optional subject name.</param>
    /// <param name="metadata">Endpoint metadata.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Subscription options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    public NatsSvcEndpoint(NatsConnection nats, string? queueGroup, string name, Func<NatsSvcMsg<T>, ValueTask> handler, string subject, IDictionary<string, string>? metadata, INatsDeserialize<T> serializer, NatsSubOpts? opts, CancellationToken cancellationToken)
        : base(nats, subject, queueGroup, opts)
    {
        _logger = nats.Opts.LoggerFactory.CreateLogger<NatsSvcEndpoint<T>>();
        _handler = handler;
        _nats = nats;
        Name = name;
        Metadata = metadata;
        _cancellationToken = cancellationToken;
        _serializer = serializer;
        _channel = Channel.CreateBounded<NatsSvcMsg<T>>(128);
        _handlerTask = Task.Run(HandlerLoop);
    }

    /// <inheritdoc/>
    public override string Name { get; }

    /// <inheritdoc/>
    public override long Requests => Volatile.Read(ref _requests);

    /// <inheritdoc/>
    public override long ProcessingTime => Volatile.Read(ref _processingTime);

    /// <inheritdoc/>
    public override long Errors => Volatile.Read(ref _errors);

    /// <inheritdoc/>
    public override string? LastError => Volatile.Read(ref _lastError);

    /// <inheritdoc/>
    public override long AverageProcessingTime => Requests == 0 ? 0 : ProcessingTime / Requests;

    /// <inheritdoc/>
    public override IDictionary<string, string>? Metadata { get; }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _handlerTask;
    }

    internal override void IncrementErrors() => Interlocked.Increment(ref _errors);

    internal override void SetLastError(string error) => Interlocked.Exchange(ref _lastError, error);

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
            msg = ParseMsg(
                activitySource: Telemetry.NatsActivities,
                activityName: "svc_receive",
                subject: subject,
                replyTo: replyTo,
                headersBuffer,
                in payloadBuffer,
                Connection,
                Connection.HeaderParser,
                serializer: _serializer);

            exception = null;
        }
        catch (Exception e)
        {
            _logger.LogError(NatsSvcLogEvents.Endpoint, e, "Endpoint {Name} error building message", Name);
            exception = e;

            // Most likely a serialization error.
            // Make sure we have a valid message
            // so handler can reply with an error.
            msg = new NatsMsg<T>(subject, replyTo, subject.Length + (replyTo?.Length ?? 0), default, default, _nats);
        }

        return _channel.Writer.WriteAsync(new NatsSvcMsg<T>(msg, this, exception), _cancellationToken);
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
                if (e is NatsSvcEndpointException epe)
                {
                    code = epe.Code;
                    message = epe.Message;
                    body = epe.Body;
                }
                else
                {
                    // Do not expose exceptions unless explicitly
                    // thrown as NatsSvcEndpointException
                    code = 999;
                    message = "Handler error";
                    body = string.Empty;

                    // Only log unknown exceptions
                    _logger.LogError(NatsSvcLogEvents.Endpoint, e, "Endpoint {Name} error processing message", Name);
                }

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
                    _logger.LogError(NatsSvcLogEvents.Endpoint, e1, "Endpoint {Name} error responding", Name);
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
