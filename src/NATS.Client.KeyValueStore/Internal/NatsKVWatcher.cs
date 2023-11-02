using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
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

    public NatsJSMsg<T?> Msg { get; init; } = default;
}

internal class NatsKVWatcher<T>
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly NatsJSContext _context;
    private readonly string _bucket;
    private readonly NatsKVWatchOpts _opts;
    private readonly NatsSubOpts? _subOpts;
    private readonly CancellationToken _cancellationToken;
    private readonly string _keyBase;
    private readonly string _filter;
    private readonly NatsConnection _nats;
    private readonly Channel<NatsKVWatchCommandMsg<T>> _commandChannel;
    private readonly Channel<NatsKVEntry<T>> _entryChannel;
    private readonly Channel<string> _consumerCreateChannel;
    private readonly Timer _timer;
    private readonly int _hbTimeout;
    private readonly long _idleHbNanos;
    private readonly Task _consumerCreateTask;
    private readonly string _stream;
    private readonly Task _commandTask;
    private readonly long _ackWaitNanos;

    private long _sequenceStream;
    private long _sequenceConsumer;
    private string _consumer;
    private volatile NatsKVWatchSub<T>? _sub;

    public NatsKVWatcher(
        NatsJSContext context,
        string bucket,
        string key,
        NatsKVWatchOpts opts,
        NatsSubOpts? subOpts,
        CancellationToken cancellationToken)
    {
        _logger = context.Connection.Opts.LoggerFactory.CreateLogger<NatsKVWatcher<T>>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _context = context;
        _bucket = bucket;
        _opts = opts;
        _subOpts = subOpts;
        _keyBase = $"$KV.{_bucket}.";
        _filter = $"{_keyBase}{key}";
        _cancellationToken = cancellationToken;
        _nats = context.Connection;
        _stream = $"KV_{_bucket}";
        _ackWaitNanos = TimeSpan.FromHours(22).ToNanos();
        _hbTimeout = (int)(opts.IdleHeartbeat * 2).TotalMilliseconds;
        _idleHbNanos = opts.IdleHeartbeat.ToNanos();
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
                        self._idleHbNanos);
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

    internal string Consumer
    {
        get => Volatile.Read(ref _consumer);
        private set => Volatile.Write(ref _consumer, value);
    }

    public async ValueTask DisposeAsync()
    {
        _nats.ConnectionDisconnected -= OnDisconnected;
        _consumerCreateChannel.Writer.TryComplete();
        _commandChannel.Writer.TryComplete();
        _entryChannel.Writer.TryComplete();
        await _consumerCreateTask;
        await _commandTask;
    }

    internal ValueTask InitAsync()
    {
        Consumer = NewNuid();
        return CreatePushConsumer("init");
    }

    private void OnDisconnected(object? sender, string e) => StopHeartbeatTimer();

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
                                        _logger.LogError("Protocol error: unexpected number ({Count}) of KV-Operation headers", operationValues.Count);
                                        return;
                                    }

                                    operation = operationValues[0] switch
                                    {
                                        "DEL" => NatsKVOperation.Del,
                                        "PURGE" => NatsKVOperation.Purge,
                                        _ => operation,
                                    };
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
                                    _logger.LogWarning("Protocol error: unexpected message subject {Subject}", msg.Subject);
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

                                    var sequence = Interlocked.Increment(ref _sequenceConsumer);

                                    if (sequence != (long)metadata.Sequence.Consumer)
                                    {
                                        CreateSub("sequence-mismatch");
                                        _logger.LogWarning("Missed messages, recreating consumer");
                                        continue;
                                    }

                                    if (_opts.IgnoreDeletes && operation is NatsKVOperation.Del or NatsKVOperation.Purge)
                                    {
                                        continue;
                                    }

                                    var delta = (long)metadata.NumPending;

                                    var entry = new NatsKVEntry<T>(_bucket, key)
                                    {
                                        Value = msg.Data,
                                        Revision = metadata.Sequence.Stream,
                                        Operation = operation,
                                        Created = metadata.Timestamp,
                                        Delta = delta,
                                    };

                                    // Increment the sequence before writing to the channel in case the channel is full
                                    // and the writer is waiting for the reader to read the message. This way the sequence
                                    // will be correctly incremented in case the timeout kicks in and recreated the consumer.
                                    Interlocked.Exchange(ref _sequenceStream, (long)metadata.Sequence.Stream);

                                    await _entryChannel.Writer.WriteAsync(entry, _cancellationToken);
                                }
                                else
                                {
                                    _logger.LogWarning("Protocol error: Message metadata is missing");
                                }
                            }
                        }
                        else if (subCommand == NatsKVWatchCommand.Ready)
                        {
                            ResetHeartbeatTimer();
                        }
                        else
                        {
                            _logger.LogError("Internal error: unexpected command {Command}", subCommand);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Command error");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected command loop error");
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
                        _logger.LogWarning(e, "Consumer create error");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected consumer create loop error");
        }
    }

    private async ValueTask CreatePushConsumer(string origin)
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

        _sub = new NatsKVWatchSub<T>(_context, _commandChannel, _subOpts, _cancellationToken);
        await _context.Connection.SubAsync(_sub, _cancellationToken).ConfigureAwait(false);

        if (_debug)
        {
            _logger.LogDebug(NatsKVLogEvents.NewDeliverySubject, "New delivery subject {Subject}", _sub.Subject);
        }

        Interlocked.Exchange(ref _sequenceConsumer, 0);

        var sequence = Volatile.Read(ref _sequenceStream);

        var config = new ConsumerConfiguration
        {
            Name = Consumer,
            DeliverPolicy = ConsumerConfigurationDeliverPolicy.all,
            AckPolicy = ConsumerConfigurationAckPolicy.none,
            DeliverSubject = _sub.Subject,
            FilterSubject = _filter,
            FlowControl = true,
            IdleHeartbeat = _idleHbNanos,
            AckWait = _ackWaitNanos,
            MaxDeliver = 1,
            MemStorage = true,
            NumReplicas = 1,
            ReplayPolicy = ConsumerConfigurationReplayPolicy.instant,
        };

        if (!_opts.IncludeHistory)
        {
            config.DeliverPolicy = ConsumerConfigurationDeliverPolicy.last_per_subject;
        }

        if (_opts.UpdatesOnly)
        {
            config.DeliverPolicy = ConsumerConfigurationDeliverPolicy.@new;
        }

        if (_opts.MetaOnly)
        {
            config.HeadersOnly = true;
        }

        if (sequence > 0)
        {
            config.DeliverPolicy = ConsumerConfigurationDeliverPolicy.by_start_sequence;
            config.OptStartSeq = sequence + 1;
        }

        await _context.CreateConsumerAsync(
            new ConsumerCreateRequest { StreamName = _stream, Config = config, },
            cancellationToken: _cancellationToken);

        if (_debug)
        {
            _logger.LogDebug(NatsKVLogEvents.NewConsumerCreated, "Created new consumer {Consumer} from {Origin}", Consumer, origin);
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
