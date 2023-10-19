using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal class NatsJSFetch<TMsg> : NatsSubBase, INatsJSFetch<TMsg>
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly Channel<NatsJSMsg<TMsg?>> _userMsgs;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly INatsSerializer _serializer;
    private readonly Timer _hbTimer;
    private readonly Timer _expiresTimer;

    private readonly long _maxMsgs;
    private readonly long _maxBytes;
    private readonly long _expires;
    private readonly long _idle;
    private readonly long _hbTimeout;

    private long _pendingMsgs;
    private long _pendingBytes;
    private int _disposed;

    public NatsJSFetch(
        long maxMsgs,
        long maxBytes,
        TimeSpan expires,
        TimeSpan idle,
        NatsJSContext context,
        string stream,
        string consumer,
        string subject,
        string? queueGroup,
        NatsSubOpts? opts)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, queueGroup, opts)
    {
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSFetch<TMsg>>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _serializer = opts?.Serializer ?? context.Connection.Opts.Serializer;

        _maxMsgs = maxMsgs;
        _maxBytes = maxBytes;
        _expires = expires.ToNanos();
        _idle = idle.ToNanos();
        _hbTimeout = (int)(idle * 2).TotalMilliseconds;
        _pendingMsgs = _maxMsgs;
        _pendingBytes = _maxBytes;

        // Keep user channel small to avoid blocking the user code when disposed,
        // otherwise channel reader will continue delivering messages even after
        // this 'fetch' object being disposed.
        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg?>>(1);
        Msgs = _userMsgs.Reader;

        if (_debug)
        {
            _logger.LogDebug(
                NatsJSLogEvents.Config,
                "Fetch setup {@Config}",
                new
                {
                    maxMsgs,
                    maxBytes,
                    expires,
                    idle,
                    _hbTimeout,
                });
        }

        _hbTimer = new Timer(
            static state =>
            {
                var self = (NatsJSFetch<TMsg>)state!;
                self.EndSubscription(NatsSubEndReason.IdleHeartbeatTimeout);
                if (self._debug)
                {
                    self._logger.LogDebug(
                        NatsJSLogEvents.IdleTimeout,
                        "Idle heartbeat timed-out after {Timeout}ns",
                        self._idle);
                }
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _expiresTimer = new Timer(
            static state =>
            {
                var self = (NatsJSFetch<TMsg>)state!;
                self.EndSubscription(NatsSubEndReason.Timeout);
                if (self._debug)
                {
                    self._logger.LogDebug(
                        NatsJSLogEvents.Expired,
                        "JetStream pull request expired {Expires}ns",
                        self._expires);
                }
            },
            this,
            expires + TimeSpan.FromSeconds(5),
            Timeout.InfiniteTimeSpan);
    }

    public ChannelReader<NatsJSMsg<TMsg?>> Msgs { get; }

    public ValueTask CallMsgNextAsync(ConsumerGetnextRequest request, CancellationToken cancellationToken = default) =>
        Connection.PubModelAsync(
            subject: $"{_context.Opts.Prefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: NatsJsonSerializer.Default,
            replyTo: Subject,
            headers: default,
            cancellationToken);

    public void ResetHeartbeatTimer() => _hbTimer.Change(_hbTimeout, Timeout.Infinite);

    public override async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await base.DisposeAsync().ConfigureAwait(false);
        await _hbTimer.DisposeAsync().ConfigureAwait(false);
        await _expiresTimer.DisposeAsync().ConfigureAwait(false);
    }

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        var request = new ConsumerGetnextRequest
        {
            Batch = _maxMsgs,
            IdleHeartbeat = _idle,
            Expires = _expires,
        };

        yield return PublishCommand<ConsumerGetnextRequest>.Create(
            pool: Connection.ObjectPool,
            subject: $"{_context.Opts.Prefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            replyTo: Subject,
            headers: default,
            value: request,
            serializer: NatsJsonSerializer.Default,
            errorHandler: default,
            cancellationToken: default);
    }

    protected override async ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        ResetHeartbeatTimer();
        if (subject == Subject)
        {
            if (headersBuffer.HasValue)
            {
                var headers = new NatsHeaders();
                if (Connection.HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
                {
                    if (headers is { Code: 408, Message: NatsHeaders.Messages.RequestTimeout })
                    {
                        EndSubscription(NatsSubEndReason.Timeout);
                    }
                    else if (headers is { Code: 409, Message: NatsHeaders.Messages.MessageSizeExceedsMaxBytes })
                    {
                        EndSubscription(NatsSubEndReason.MaxBytes);
                    }
                    else if (headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat })
                    {
                    }
                    else if (headers.HasTerminalJSError())
                    {
                        _userMsgs.Writer.TryComplete(new NatsJSProtocolException($"JetStream server error: {headers.Code} {headers.MessageText}"));
                        EndSubscription(NatsSubEndReason.JetStreamError);
                    }
                    else
                    {
                        if (_debug)
                        {
                            _logger.LogDebug(
                                NatsJSLogEvents.ProtocolMessage,
                                "Protocol message: {Code} {Description}",
                                headers.Code,
                                headers.MessageText);
                        }
                    }
                }
                else
                {
                    _logger.LogError(
                        NatsJSLogEvents.Headers,
                        "Can't parse headers: {HeadersBuffer}",
                        Encoding.ASCII.GetString(headersBuffer.Value.ToArray()));
                    throw new NatsJSException("Can't parse headers");
                }
            }
            else
            {
                throw new NatsJSException("No header found");
            }
        }
        else
        {
            var msg = new NatsJSMsg<TMsg?>(
                NatsMsg<TMsg?>.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    _serializer),
                _context);

            _pendingMsgs--;
            _pendingBytes -= msg.Size;

            // Stop feeding the user if we are disposed.
            // We need to exit as soon as possible.
            if (Volatile.Read(ref _disposed) == 0)
            {
                await _userMsgs.Writer.WriteAsync(msg).ConfigureAwait(false);
            }
        }

        if (_maxBytes > 0 && _pendingBytes <= 0)
        {
            EndSubscription(NatsSubEndReason.MaxBytes);
        }
        else if (_maxBytes == 0 && _pendingMsgs == 0)
        {
            EndSubscription(NatsSubEndReason.MaxMsgs);
        }
    }

    protected override void TryComplete()
    {
        _userMsgs.Writer.TryComplete();
    }
}
