using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal enum NatsJSOrderedPushConsumerCommand
{
    Msg,
    Ready,
}

internal readonly struct NatsJSOrderedPushConsumerMsg<T>
{
    public NatsJSOrderedPushConsumerMsg()
    {
    }

    public NatsJSOrderedPushConsumerCommand Command { get; init; } = default;

    public NatsJSMsg<T> Msg { get; init; } = default;
}

internal record NatsJSOrderedPushConsumerOpts
{
    /// <summary>
    /// Default watch options
    /// </summary>
    public static readonly NatsJSOrderedPushConsumerOpts Default = new();

    /// <summary>
    /// Idle heartbeat interval
    /// </summary>
    public TimeSpan IdleHeartbeat { get; init; } = TimeSpan.FromSeconds(5);

    public ConsumerConfigDeliverPolicy DeliverPolicy { get; init; } = ConsumerConfigDeliverPolicy.All;

    public bool HeadersOnly { get; init; } = false;

    /// <summary>
    /// Timeout for cleanup operations during disposal (e.g. deleting ephemeral consumers).
    /// If the server is slow or unresponsive, disposal will not block longer than this.
    /// </summary>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

internal class NatsJSOrderedPushConsumer<T>
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly INatsJSContext _context;
    private readonly string _stream;
    private readonly string _filter;
    private readonly INatsDeserialize<T> _serializer;
    private readonly NatsJSOrderedPushConsumerOpts _opts;
    private readonly NatsSubOpts? _subOpts;
    private readonly CancellationToken _cancellationToken;
    private readonly INatsConnection _nats;
    private readonly Channel<NatsJSOrderedPushConsumerMsg<T>> _commandChannel;
    private readonly Channel<NatsJSMsg<T>> _msgChannel;
    private readonly Channel<string> _consumerCreateChannel;
    private readonly Timer _timer;
    private readonly int _hbTimeout;
    private readonly Task _consumerCreateTask;
    private readonly Task _commandTask;

    private ulong _sequenceStream;
    private ulong _sequenceConsumer;
    private string _consumer;
    private volatile NatsJSOrderedPushConsumerSub<T>? _sub;
    private int _done;

    public NatsJSOrderedPushConsumer(
        INatsJSContext context,
        string stream,
        string filter,
        INatsDeserialize<T> serializer,
        NatsJSOrderedPushConsumerOpts opts,
        NatsSubOpts? subOpts,
        CancellationToken cancellationToken)
    {
        _logger = context.Connection.Opts.LoggerFactory.CreateLogger<NatsJSOrderedPushConsumer<T>>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _context = context;
        _stream = stream;
        _filter = filter;
        _serializer = serializer;
        _opts = opts;
        _subOpts = subOpts;
        _cancellationToken = cancellationToken;
        _nats = context.Connection;
        _hbTimeout = (int)new TimeSpan(opts.IdleHeartbeat.Ticks * 2).TotalMilliseconds;
        _consumer = NewNuid();

        _nats.ConnectionDisconnected += OnDisconnected;

        _timer = new Timer(
            static state =>
            {
                var self = (NatsJSOrderedPushConsumer<T>)state!;
                self.CreateSub("idle-heartbeat-timeout");
                if (self._debug)
                {
                    self._logger.LogDebug(
                        NatsJSLogEvents.IdleTimeout,
                        "Idle heartbeat timeout after {Timeout}ns",
                        self._opts.IdleHeartbeat);
                }
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        // Use connection's channel options (default DropNewest) to avoid blocking socket reads.
        // When messages are dropped from command channel, notify via OnMessageDropped callback.
        _commandChannel = Channel.CreateBounded<NatsJSOrderedPushConsumerMsg<T>>(
            _nats.GetBoundedChannelOpts(subOpts?.ChannelOpts),
            cmd =>
            {
                // Only notify for actual messages, not control commands like Ready
                if (cmd.Command == NatsJSOrderedPushConsumerCommand.Msg)
                {
                    var sub = _sub;
                    if (sub != null)
                    {
                        // cmd.Msg is guaranteed to be valid when Command == Msg
                        _nats.OnMessageDropped(sub, _commandChannel?.Reader.Count ?? 0, cmd.Msg.Msg);
                    }
                }
            });

        // Message channel also uses DropNewest to prevent blocking the command processing loop.
        _msgChannel = Channel.CreateBounded<NatsJSMsg<T>>(
            _nats.GetBoundedChannelOpts(subOpts?.ChannelOpts),
            msg =>
            {
                var sub = _sub;
                if (sub != null)
                {
                    _nats.OnMessageDropped(sub, _msgChannel?.Reader.Count ?? 0, msg.Msg);
                }
            });

        // A single request to create the consumer is enough because we don't want to create a new consumer
        // back to back in case the consumer is being recreated due to a timeout and a mismatch in consumer
        // sequence for example; creating the consumer once would solve both the issues.
        _consumerCreateChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        _consumerCreateTask = Task.Run(ConsumerCreateLoop);
        _commandTask = Task.Run(CommandLoop);
    }

    public ChannelReader<NatsJSMsg<T>> Msgs => _msgChannel.Reader;

    public bool IsDone => Volatile.Read(ref _done) > 0;

    private string Consumer
    {
        get => Volatile.Read(ref _consumer);
        set => Volatile.Write(ref _consumer, value);
    }

    public async ValueTask DisposeAsync()
    {
        _nats.ConnectionDisconnected -= OnDisconnected;

#if NETSTANDARD2_0
        _timer.Dispose();
#else
        await _timer.DisposeAsync().ConfigureAwait(false);
#endif

        // For correctly Dispose,
        // first stop the consumer Creation operations and then the command execution operations.
        // It is necessary that all consumerCreation operations have time to complete before command CommandLoop stop
        _consumerCreateChannel.Writer.TryComplete();
        await _consumerCreateTask;

        _commandChannel.Writer.TryComplete();
        await _commandTask;

        _msgChannel.Writer.TryComplete();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        cts.CancelAfter(_opts.CleanupTimeout);
        try
        {
            await _context.DeleteConsumerAsync(_stream, Consumer, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout - server will clean up ephemeral consumer automatically
        }
        catch (NatsJSApiException)
        {
            // Consumer may already be gone, that's fine
        }
    }

    internal void Init()
    {
        Consumer = NewNuid();
        CreateSub("init");
    }

    internal void Done()
    {
        Interlocked.Increment(ref _done);
        _msgChannel.Writer.TryComplete();
    }

    private ValueTask OnDisconnected(object? sender, NatsEventArgs args)
    {
        StopHeartbeatTimer();
        return default;
    }

    private async Task CommandLoop()
    {
        try
        {
            while (await _commandChannel.Reader.WaitToReadAsync(_cancellationToken))
            {
                while (_commandChannel.Reader.TryRead(out var command))
                {
                    try
                    {
                        var subCommand = command.Command;

                        if (subCommand == NatsJSOrderedPushConsumerCommand.Msg)
                        {
                            ResetHeartbeatTimer();
                            var msg = command.Msg;

                            var subSubject = _sub?.Subject;

                            if (subSubject == null)
                                continue;

                            if (string.Equals(msg.Subject, subSubject))
                            {
                                // Control messages: e.g. heartbeat
                                if (msg.Headers is { } headers)
                                {
                                    if (headers.TryGetValue("Nats-Consumer-Stalled", out var flowControlReplyTo))
                                    {
                                        await _nats.PublishAsync(flowControlReplyTo, cancellationToken: _cancellationToken);
                                    }

                                    if (headers is { Code: 100, MessageText: "FlowControl Request" })
                                    {
#pragma warning disable CS0618 // Type or member is obsolete
                                        await msg.ReplyAsync(cancellationToken: _cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
                                    }
                                }
                            }
                            else
                            {
                                if (msg.Metadata is { } metadata)
                                {
                                    if (!metadata.Consumer.Equals(Consumer))
                                    {
                                        // Ignore messages from other consumers
                                        // This might happen if the consumer is recreated
                                        // and the old consumer somehow still receives messages
                                        continue;
                                    }

#if NETSTANDARD
                                    var sequence = InterlockedEx.Increment(ref _sequenceConsumer);
#else
                                    var sequence = Interlocked.Increment(ref _sequenceConsumer);
#endif

                                    if (sequence != metadata.Sequence.Consumer)
                                    {
                                        CreateSub("sequence-mismatch");
                                        _logger.LogWarning(NatsJSLogEvents.RecreateConsumer, "Missed messages, recreating consumer");
                                        continue;
                                    }

                                    if (!IsDone)
                                    {
                                        // Try to write to the message channel
                                        // If the channel is full, TryWrite returns false
                                        // In that case, we DON'T update _sequenceStream so recovery can re-fetch
                                        if (_msgChannel.Writer.TryWrite(msg))
                                        {
                                            // Only update sequence after successful write
#if NETSTANDARD
                                            InterlockedEx.Exchange(ref _sequenceStream, metadata.Sequence.Stream);
#else
                                            Interlocked.Exchange(ref _sequenceStream, metadata.Sequence.Stream);
#endif
                                        }
                                        else
                                        {
                                            // Message was dropped - notify and trigger recovery
                                            var sub = _sub;
                                            if (sub != null)
                                            {
                                                _nats.OnMessageDropped(sub, _msgChannel.Reader.Count, msg.Msg);
                                            }

                                            CreateSub("message-channel-full");
                                            _logger.LogWarning(NatsJSLogEvents.RecreateConsumer, "Message channel full, recreating consumer");
                                        }
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning(NatsJSLogEvents.ProtocolMessage, "Protocol error: Message metadata is missing");
                                }
                            }
                        }
                        else if (subCommand == NatsJSOrderedPushConsumerCommand.Ready)
                        {
                            ResetHeartbeatTimer();
                        }
                        else
                        {
                            _logger.LogError(NatsJSLogEvents.Internal, "Internal error: unexpected command {Command}", subCommand);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(NatsJSLogEvents.Internal, e, "Command error");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(NatsJSLogEvents.Internal, e, "Unexpected command loop error");
        }
    }

    private async Task ConsumerCreateLoop()
    {
        try
        {
            while (await _consumerCreateChannel.Reader.WaitToReadAsync(_cancellationToken))
            {
                while (_consumerCreateChannel.Reader.TryRead(out var origin))
                {
                    try
                    {
                        await CreatePushConsumer(origin);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(NatsJSLogEvents.RecreateConsumer, e, "Consumer create error");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(NatsJSLogEvents.Internal, e, "Unexpected consumer create loop error");
        }
    }

    private async ValueTask CreatePushConsumer(string origin)
    {
        if (_debug)
        {
            _logger.LogDebug(NatsJSLogEvents.NewConsumer, "Creating new consumer {Consumer} from {Origin}", Consumer, origin);
        }

        if (_sub != null)
        {
            if (_debug)
            {
                _logger.LogDebug(NatsJSLogEvents.DeleteOldDeliverySubject, "Deleting old delivery subject {Subject}", _sub.Subject);
            }

            await _sub.UnsubscribeAsync();
            await _sub.DisposeAsync();
        }

        _sub = new NatsJSOrderedPushConsumerSub<T>(_context, _commandChannel, _serializer, _subOpts, _cancellationToken);
        await _context.Connection.AddSubAsync(_sub, _cancellationToken).ConfigureAwait(false);

        if (_debug)
        {
            _logger.LogDebug(NatsJSLogEvents.NewDeliverySubject, "New delivery subject {Subject}", _sub.Subject);
        }

#if NETSTANDARD
        InterlockedEx.Exchange(ref _sequenceConsumer, 0);
#else
        Interlocked.Exchange(ref _sequenceConsumer, 0);
#endif

        var sequence = Volatile.Read(ref _sequenceStream);

        var config = new ConsumerConfig
        {
            Name = Consumer,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.None,
            DeliverSubject = _sub.Subject,
            FilterSubject = _filter,
            FlowControl = true,
            IdleHeartbeat = _opts.IdleHeartbeat,
            AckWait = TimeSpan.FromHours(22),
            MaxDeliver = 1,
            MemStorage = true,
            NumReplicas = 1,
            ReplayPolicy = ConsumerConfigReplayPolicy.Instant,
        };

        config.DeliverPolicy = _opts.DeliverPolicy;
        config.HeadersOnly = _opts.HeadersOnly;

        if (sequence > 0)
        {
            config.DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence;
            config.OptStartSeq = sequence + 1;
        }

        await _context.CreateOrUpdateConsumerAsync(
            _stream,
            config,
            cancellationToken: _cancellationToken);

        if (_debug)
        {
            _logger.LogDebug(NatsJSLogEvents.NewConsumerCreated, "Created new consumer {Consumer} from {Origin}", Consumer, origin);
        }
    }

    private string NewNuid()
    {
        Span<char> buffer = stackalloc char[22];
        if (Nuid.TryWriteNuid(buffer))
        {
            return buffer.ToString();
        }

        throw new InvalidOperationException("Internal error: can't generate nuid");
    }

    private void ResetHeartbeatTimer() => _timer.Change(_hbTimeout, Timeout.Infinite);

    private void StopHeartbeatTimer() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    private void CreateSub(string origin)
    {
        Consumer = NewNuid();
        _consumerCreateChannel.Writer.TryWrite(origin);
    }
}

internal class NatsJSOrderedPushConsumerSub<T> : NatsSubBase
{
    private readonly INatsJSContext _context;
    private readonly CancellationToken _cancellationToken;
    private readonly INatsConnection _nats;
    private readonly NatsHeaderParser _headerParser;
    private readonly INatsDeserialize<T> _serializer;
    private readonly ChannelWriter<NatsJSOrderedPushConsumerMsg<T>> _commands;

    public NatsJSOrderedPushConsumerSub(
        INatsJSContext context,
        Channel<NatsJSOrderedPushConsumerMsg<T>> commandChannel,
        INatsDeserialize<T> serializer,
        NatsSubOpts? opts,
        CancellationToken cancellationToken)
        : base(
            connection: context.Connection,
            manager: context.Connection.SubscriptionManager,
            subject: context.NewBaseInbox(),
            queueGroup: default,
            opts)
    {
        _context = context;
        _cancellationToken = cancellationToken;
        _serializer = serializer;
        _nats = context.Connection;
        _headerParser = _nats.HeaderParser;
        _commands = commandChannel.Writer;
        _nats.ConnectionOpened += OnConnectionOpened;
    }

    public override async ValueTask ReadyAsync()
    {
        await base.ReadyAsync();
        await _commands.WriteAsync(new NatsJSOrderedPushConsumerMsg<T> { Command = NatsJSOrderedPushConsumerCommand.Ready }, _cancellationToken).ConfigureAwait(false);
    }

    public override ValueTask DisposeAsync()
    {
        _nats.ConnectionOpened -= OnConnectionOpened;
        return base.DisposeAsync();
    }

    protected override async ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        var msg = new NatsJSMsg<T>(NatsMsg<T>.Build(subject, replyTo, headersBuffer, payloadBuffer, _nats, _headerParser, _serializer), _context);
        await _commands.WriteAsync(new NatsJSOrderedPushConsumerMsg<T> { Command = NatsJSOrderedPushConsumerCommand.Msg, Msg = msg }, _cancellationToken).ConfigureAwait(false);
    }

    protected override void TryComplete()
    {
    }

    private ValueTask OnConnectionOpened(object? sender, NatsEventArgs args)
    {
        // result is discarded, so this code is assumed to not be failing
        _ = _commands.TryWrite(new NatsJSOrderedPushConsumerMsg<T> { Command = NatsJSOrderedPushConsumerCommand.Ready });
        return default;
    }
}
