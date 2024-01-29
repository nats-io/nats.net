using System.Buffers;
using System.Diagnostics;
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
}

internal class NatsJSOrderedPushConsumer<T>
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _filter;
    private readonly INatsDeserialize<T> _serializer;
    private readonly NatsJSOrderedPushConsumerOpts _opts;
    private readonly NatsSubOpts? _subOpts;
    private readonly CancellationToken _cancellationToken;
    private readonly NatsConnection _nats;
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
        NatsJSContext context,
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
        _hbTimeout = (int)(opts.IdleHeartbeat * 2).TotalMilliseconds;
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

        // Channel size 1 is enough because we want backpressure to go all the way to the subscription
        // so that we get most accurate view of the stream. We can keep them as 1 until we find a case
        // where it's not enough due to performance for example.
        _commandChannel = Channel.CreateBounded<NatsJSOrderedPushConsumerMsg<T>>(1);
        _msgChannel = Channel.CreateBounded<NatsJSMsg<T>>(1);
        Msgs = new ActivityEndingJSMsgReader<T>(_msgChannel.Reader);

        // A single request to create the consumer is enough because we don't want to create a new consumer
        // back to back in case the consumer is being recreated due to a timeout and a mismatch in consumer
        // sequence for example; creating the consumer once would solve both the issues.
        _consumerCreateChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(1) { AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.DropOldest, });

        _consumerCreateTask = Task.Run(ConsumerCreateLoop);
        _commandTask = Task.Run(CommandLoop);
    }

    public ChannelReader<NatsJSMsg<T>> Msgs { get; }

    public bool IsDone => Volatile.Read(ref _done) > 0;

    private string Consumer
    {
        get => Volatile.Read(ref _consumer);
        set => Volatile.Write(ref _consumer, value);
    }

    public async ValueTask DisposeAsync()
    {
        _nats.ConnectionDisconnected -= OnDisconnected;

        _consumerCreateChannel.Writer.TryComplete();
        _commandChannel.Writer.TryComplete();
        _msgChannel.Writer.TryComplete();

        await _consumerCreateTask;
        await _commandTask;

        await _context.DeleteConsumerAsync(Telemetry.NatsInternalActivities, _stream, Consumer, _cancellationToken);
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
                                        await _nats.PublishNoneAsync(Telemetry.NatsInternalActivities, subject: flowControlReplyTo, cancellationToken: _cancellationToken);
                                    }

                                    if (headers is { Code: 100, MessageText: "FlowControl Request" })
                                    {
                                        await msg.ReplyAsync(cancellationToken: _cancellationToken);
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

                                    var sequence = Interlocked.Increment(ref _sequenceConsumer);

                                    if (sequence != metadata.Sequence.Consumer)
                                    {
                                        CreateSub("sequence-mismatch");
                                        _logger.LogWarning(NatsJSLogEvents.RecreateConsumer, "Missed messages, recreating consumer");
                                        continue;
                                    }

                                    // Increment the sequence before writing to the channel in case the channel is full
                                    // and the writer is waiting for the reader to read the message. This way the sequence
                                    // will be correctly incremented in case the timeout kicks in and recreated the consumer.
                                    Interlocked.Exchange(ref _sequenceStream, metadata.Sequence.Stream);

                                    if (!IsDone)
                                    {
                                        try
                                        {
                                            await _msgChannel.Writer.WriteAsync(msg, _cancellationToken);
                                        }
                                        catch
                                        {
                                            if (!IsDone)
                                                throw;
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
        await _context.Connection.SubAsync(_sub, _cancellationToken).ConfigureAwait(false);

        if (_debug)
        {
            _logger.LogDebug(NatsJSLogEvents.NewDeliverySubject, "New delivery subject {Subject}", _sub.Subject);
        }

        Interlocked.Exchange(ref _sequenceConsumer, 0);

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
        if (NuidWriter.TryWriteNuid(buffer))
        {
            return new string(buffer);
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
    private readonly NatsJSContext _context;
    private readonly CancellationToken _cancellationToken;
    private readonly NatsConnection _nats;
    private readonly NatsHeaderParser _headerParser;
    private readonly INatsDeserialize<T> _serializer;
    private readonly ChannelWriter<NatsJSOrderedPushConsumerMsg<T>> _commands;

    public NatsJSOrderedPushConsumerSub(
        NatsJSContext context,
        Channel<NatsJSOrderedPushConsumerMsg<T>> commandChannel,
        INatsDeserialize<T> serializer,
        NatsSubOpts? opts,
        CancellationToken cancellationToken)
        : base(
            connection: context.Connection,
            manager: context.Connection.SubscriptionManager,
            subject: context.NewInbox(),
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
        var msg = new NatsJSMsg<T>(
            ParseMsg(
                activitySource: Telemetry.NatsInternalActivities,
                activityName: "js_receive",
                subject: subject,
                replyTo: replyTo,
                headersBuffer,
                in payloadBuffer,
                Connection,
                Connection.HeaderParser,
                serializer: _serializer),
            _context);

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
