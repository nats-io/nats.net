using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NATS.Client.KeyValueStore.Internal;

internal enum NatsKVWatchCommand
{
    Msg,
    Ready,
}

internal readonly struct NatsKVWatchCommandMsg<T>
{
    public NatsKVWatchCommandMsg()
    {
    }

    public NatsKVWatchCommand Command { get; init; } = default;

    public NatsJSMsg<T> Msg { get; init; } = default;
}

internal class NatsKVWatcher<T> : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly NatsJSContext _context;
    private readonly string _bucket;
    private readonly INatsDeserialize<T> _serializer;
    private readonly NatsKVWatchOpts _opts;
    private readonly NatsSubOpts? _subOpts;
    private readonly CancellationToken _cancellationToken;
    private readonly string _keyBase;
    private readonly string[] _filters;
    private readonly INatsConnection _nats;
    private readonly Channel<NatsKVWatchCommandMsg<T>> _commandChannel;
    private readonly Channel<NatsKVEntry<T>> _entryChannel;
    private readonly Channel<string> _consumerCreateChannel;
    private readonly Timer _timer;
    private readonly int _hbTimeout;
    private readonly Task _consumerCreateTask;
    private readonly string _stream;
    private readonly Task _commandTask;

    private ulong _sequenceStream;
    private ulong _sequenceConsumer;
    private string _consumer;
    private volatile NatsKVWatchSub<T>? _sub;
    private INatsJSConsumer? _initialConsumer;

    public NatsKVWatcher(
        NatsJSContext context,
        string bucket,
        IEnumerable<string> keys,
        INatsDeserialize<T> serializer,
        NatsKVWatchOpts opts,
        NatsSubOpts? subOpts,
        CancellationToken cancellationToken)
    {
        _logger = context.Connection.Opts.LoggerFactory.CreateLogger<NatsKVWatcher<T>>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _context = context;
        _bucket = bucket;
        _serializer = serializer;
        _opts = opts;
        _subOpts = subOpts;
        _keyBase = $"$KV.{_bucket}.";
        _filters = keys.Select(key => $"{_keyBase}{key}").ToArray();

        if (_filters.Length == 0)
        {
            // Without this check we'd get an error from the server:
            // 'consumer delivery policy is deliver last per subject, but optional filter subject is not set'
            throw new ArgumentException("At least one key must be provided", nameof(keys));
        }

        _cancellationToken = cancellationToken;
        _nats = context.Connection;
        _stream = $"KV_{_bucket}";
        _hbTimeout = (int)new TimeSpan(opts.IdleHeartbeat.Ticks * 2).TotalMilliseconds;
        _consumer = NewNuid();

        _nats.ConnectionDisconnected += OnDisconnected;

        _timer = new Timer(
            static state =>
            {
                var self = (NatsKVWatcher<T>)state!;
                self.CreateSub("idle-heartbeat-timeout");
                if (self._debug)
                {
                    self._logger.LogDebug(
                        NatsKVLogEvents.IdleTimeout,
                        "Idle heartbeat timeout after {Timeout}ns",
                        self._opts.IdleHeartbeat);
                }
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        // Keep the channel size large enough to avoid blocking the connection
        // TCP receiver thread in case other operations are in-flight.
        _commandChannel = Channel.CreateBounded<NatsKVWatchCommandMsg<T>>(1000);
        _entryChannel = Channel.CreateBounded<NatsKVEntry<T>>(1000);

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

    public ChannelReader<NatsKVEntry<T>> Entries => _entryChannel.Reader;

    internal INatsJSConsumer InitialConsumer
    {
        get => _initialConsumer ?? throw new InvalidOperationException("Consumer not initialized");
        private set => _initialConsumer = value;
    }

    internal string Consumer
    {
        get => Volatile.Read(ref _consumer);
        private set => Volatile.Write(ref _consumer, value);
    }

    public async ValueTask DisposeAsync()
    {
        _nats.ConnectionDisconnected -= OnDisconnected;

        if (_sub != null)
        {
            await _sub.DisposeAsync();
        }

        _consumerCreateChannel.Writer.TryComplete();
        _commandChannel.Writer.TryComplete();
        _entryChannel.Writer.TryComplete();
        await _consumerCreateTask;
        await _commandTask;
    }

    internal async ValueTask InitAsync()
    {
        Consumer = NewNuid();
        InitialConsumer = await CreatePushConsumer("init");
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

                        if (subCommand == NatsKVWatchCommand.Msg)
                        {
                            ResetHeartbeatTimer();
                            var msg = command.Msg;

                            var operation = NatsKVOperation.Put;

                            if (msg.Headers is { } headers)
                            {
                                if (headers.TryGetValue("KV-Operation", out var operationValues))
                                {
                                    if (operationValues.Count != 1)
                                    {
                                        var exception = new NatsKVException("Message metadata is missing");
                                        _entryChannel.Writer.TryComplete(exception);
                                        _logger.LogError(NatsKVLogEvents.Protocol, "Protocol error: unexpected number ({Count}) of KV-Operation headers", operationValues.Count);
                                        return;
                                    }

                                    operation = operationValues[0] switch
                                    {
                                        "DEL" => NatsKVOperation.Del,
                                        "PURGE" => NatsKVOperation.Purge,
                                        _ => operation,
                                    };
                                }

                                if (headers is { Code: 100, MessageText: "FlowControl Request" })
                                {
                                    await msg.ReplyAsync(cancellationToken: _cancellationToken);
                                    continue;
                                }
                            }

                            var subSubject = _sub?.Subject;

                            if (subSubject == null)
                                continue;

                            if (string.Equals(msg.Subject, subSubject))
                            {
                                // Control message: e.g. heartbeat
                            }
                            else
                            {
                                if (msg.Subject.Length <= _keyBase.Length)
                                {
                                    _logger.LogWarning(NatsKVLogEvents.Protocol, "Protocol error: unexpected message subject {Subject}", msg.Subject);
                                    continue;
                                }

                                var key = msg.Subject.Substring(_keyBase.Length);

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
                                        _logger.LogWarning(NatsKVLogEvents.RecreateConsumer, "Missed messages, recreating consumer");
                                        continue;
                                    }

                                    if (_opts.IgnoreDeletes && operation is NatsKVOperation.Del or NatsKVOperation.Purge)
                                    {
                                        continue;
                                    }

                                    var delta = metadata.NumPending;

                                    var entry = new NatsKVEntry<T>(_bucket, key)
                                    {
                                        Value = msg.Data,
                                        Revision = metadata.Sequence.Stream,
                                        Operation = operation,
                                        Created = metadata.Timestamp,
                                        Delta = delta,
                                        Error = msg.Error,
                                    };

                                    // Increment the sequence before writing to the channel in case the channel is full
                                    // and the writer is waiting for the reader to read the message. This way the sequence
                                    // will be correctly incremented in case the timeout kicks in and recreated the consumer.
#if NETSTANDARD
                                    InterlockedEx.Exchange(ref _sequenceStream, metadata.Sequence.Stream);
#else
                                    Interlocked.Exchange(ref _sequenceStream, metadata.Sequence.Stream);
#endif

                                    await _entryChannel.Writer.WriteAsync(entry, _cancellationToken);
                                }
                                else
                                {
                                    _logger.LogWarning(NatsKVLogEvents.Protocol, "Protocol error: Message metadata is missing");
                                }
                            }
                        }
                        else if (subCommand == NatsKVWatchCommand.Ready)
                        {
                            ResetHeartbeatTimer();
                        }
                        else
                        {
                            _logger.LogError(NatsKVLogEvents.Internal, "Internal error: unexpected command {Command}", subCommand);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(NatsKVLogEvents.Internal, e, "Command error");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(NatsKVLogEvents.Internal, e, "Unexpected command loop error");
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
                        _logger.LogWarning(NatsKVLogEvents.NewConsumer, e, "Consumer create error");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(NatsKVLogEvents.Internal, e, "Unexpected consumer create loop error");
        }
    }

    private async ValueTask<INatsJSConsumer> CreatePushConsumer(string origin)
    {
        if (_debug)
        {
            _logger.LogDebug(NatsKVLogEvents.NewConsumer, "Creating new consumer {Consumer} from {Origin}", Consumer, origin);
        }

        if (_sub != null)
        {
            if (_debug)
            {
                _logger.LogDebug(NatsKVLogEvents.DeleteOldDeliverySubject, "Deleting old delivery subject {Subject}", _sub.Subject);
            }

            await _sub.UnsubscribeAsync();
            await _sub.DisposeAsync();
        }

        _sub = new NatsKVWatchSub<T>(_context, _commandChannel, _serializer, _subOpts, _cancellationToken);
        await _context.Connection.SubAsync(_sub, _cancellationToken).ConfigureAwait(false);

        if (_debug)
        {
            _logger.LogDebug(NatsKVLogEvents.NewDeliverySubject, "New delivery subject {Subject}", _sub.Subject);
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
            FlowControl = true,
            IdleHeartbeat = _opts.IdleHeartbeat,
            AckWait = TimeSpan.FromHours(22),
            MaxDeliver = 1,
            MemStorage = true,
            NumReplicas = 1,
            ReplayPolicy = ConsumerConfigReplayPolicy.Instant,
        };

        // Use FilterSubject (singular) when there is only one filter
        // This is for compatibility with older NATS servers (<2.10)
        if (_filters.Length == 1)
        {
            config.FilterSubject = _filters[0];
        }

        // Use FilterSubjects (plural) when there are multiple filters
        // This is for compatibility with newer NATS servers (>=2.10)
        if (_filters.Length > 1)
        {
            config.FilterSubjects = _filters;
        }

        if (!_opts.IncludeHistory)
        {
            config.DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject;
        }

        if (_opts.UpdatesOnly)
        {
            config.DeliverPolicy = ConsumerConfigDeliverPolicy.New;
        }

        if (_opts.MetaOnly)
        {
            config.HeadersOnly = true;
        }

        // Resume from a specific revision ?
        if (sequence > 0)
        {
            config.DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence;
            config.OptStartSeq = sequence + 1;
        }
        else if (_opts.ResumeAtRevision > 0)
        {
            config.DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence;
            config.OptStartSeq = _opts.ResumeAtRevision;
        }

        var consumer = await _context.CreateOrUpdateConsumerAsync(
            _stream,
            config,
            cancellationToken: _cancellationToken);

        if (_debug)
        {
            _logger.LogDebug(NatsKVLogEvents.NewConsumerCreated, "Created new consumer {Consumer} from {Origin}", Consumer, origin);
        }

        return consumer;
    }

    private string NewNuid()
    {
        Span<char> buffer = stackalloc char[22];
        if (NuidWriter.TryWriteNuid(buffer))
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
