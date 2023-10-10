using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
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

internal class NatsKVWatcher<T> : INatsKVWatcher<T>
{
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly NatsJSContext _context;
    private readonly string _bucket;
    private readonly string _key;
    private readonly NatsKVWatchOpts? _opts;
    private readonly CancellationToken _cancellationToken;
    private readonly string _keyBase;
    private readonly string _filter;
    private readonly INatsSerializer _serializer;
    private readonly NatsConnection _nats;
    private readonly NatsHeaderParser _headerParser;
    private readonly Channel<NatsKVWatchCommandMsg<T>> _commandChannel;
    private readonly Channel<NatsKVEntry<T?>> _entryChannel;
    private readonly Channel<string> _consumerCreateChannel;
    private readonly Timer _timer;
    private readonly int _hbTimeout;
    private readonly long _idleHbNanos;
    private readonly long _inactiveThresholdNanos;
    private readonly Task _consumerCreateTask;
    private readonly string _stream;
    private readonly Task _commandTask;

    private long _sequenceStream;
    private long _sequenceConsumer;
    private string? _consumer;
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
        _key = key;
        _opts = opts;
        _keyBase = $"$KV.{_bucket}.";
        _filter = $"{_keyBase}{key}";
        _cancellationToken = cancellationToken;
        _serializer = subOpts?.Serializer ?? context.Connection.Opts.Serializer;
        _nats = context.Connection;
        _headerParser = _nats.HeaderParser;
        _stream = $"KV_{_bucket}";

        _nats.ConnectionDisconnected+= OnDisconnected;
        _hbTimeout = (int)(_opts.IdleHeartbeat * 2).TotalMilliseconds;

        _idleHbNanos = _opts.IdleHeartbeat.ToNanos();
        _inactiveThresholdNanos = _opts.InactiveThreshold.ToNanos();

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

        _commandChannel = Channel.CreateBounded<NatsKVWatchCommandMsg<T>>(1);
        _entryChannel = Channel.CreateBounded<NatsKVEntry<T?>>(1);

        _consumerCreateChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        _consumerCreateTask = Task.Run(ConsumerCreateLoop);
        _commandTask = Task.Run(CommandLoop);
    }

    private void OnDisconnected(object? sender, string e)
    {
        StopHeartbeatTimer();
    }

    public ChannelReader<NatsKVEntry<T?>> Msgs => _entryChannel.Reader;

    internal void Init() => CreateSub("init");

    internal string? Consumer
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

    private async Task CommandLoop()
    {
        try
        {
            while (await _commandChannel.Reader.WaitToReadAsync(_cancellationToken))
            {
                while (_commandChannel.Reader.TryRead(out var command))
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
                                    "DEL" => NatsKVOperation.Delete,
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
                            if (msg.Metadata is { } metadata)
                            {

                                // Very first message: Consumer creation response might not arrive
                                // before the first message so we rely on the message metadata for
                                // the consumer name.
                                if (Consumer == null)
                                {
                                    Consumer = metadata.Consumer;
                                }

                                if (!metadata.Consumer.Equals(Consumer))
                                {
                                    // Ignore messages from other consumers
                                    // This might happen if the consumer is recreated
                                    // and the old consumer somehow still receives messages
                                    continue;
                                }

                                var sequence = Interlocked.Increment(ref _sequenceConsumer);

                                if (sequence != (long) metadata.Sequence.Consumer)
                                {
                                    CreateSub("sequence-mismatch");
                                    _logger.LogWarning("Missed messages, recreating consumer");
                                    continue;
                                }

                                var key = msg.Subject.Substring(_keyBase.Length);

                                var entry = new NatsKVEntry<T?>(_bucket, key)
                                {
                                    Value = msg.Data, Revision = (long) metadata.Sequence.Stream, Operation = operation, Created = metadata.Timestamp,
                                };

                                await _entryChannel.Writer.WriteAsync(entry, _cancellationToken);

                                Interlocked.Exchange(ref _sequenceStream, (long) metadata.Sequence.Stream);
                            }
                            else
                            {
                                var exception = new NatsKVException("Message metadata is missing");
                                _entryChannel.Writer.TryComplete(exception);
                                _logger.LogError("Protocol error: Message metadata is missing");
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
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Command loop error: {e}");
            _logger.LogError(e, "Command loop error");
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
                    if (_debug)
                    {
                        _logger.LogDebug(NatsKVLogEvents.NewConsumer, "Creating new consumer from {Origin}", origin);
                    }

                    if (_sub != null)
                    {
                        Console.WriteLine($"UNSUB {_sub.Subject}");
                        await _sub.UnsubscribeAsync();
                        await _sub.DisposeAsync();
                    }

                    _sub = new NatsKVWatchSub<T>(_context, _commandChannel, opts: default, _cancellationToken);
                    await _context.Connection.SubAsync(_sub, _cancellationToken).ConfigureAwait(false);
                    Console.WriteLine($"SUB {_sub.Subject}");

                    Consumer = null;
                    Interlocked.Exchange(ref _sequenceConsumer, 0);

                    var sequence = Volatile.Read(ref _sequenceStream);

                    var config = new ConsumerConfiguration
                    {
                        DeliverPolicy = ConsumerConfigurationDeliverPolicy.last_per_subject,
                        AckPolicy = ConsumerConfigurationAckPolicy.none,
                        DeliverSubject = _sub.Subject,
                        FilterSubject = _filter,
                        FlowControl = true,
                        IdleHeartbeat = _idleHbNanos,
                        AckWait = TimeSpan.FromHours(22).ToNanos(),
                        MaxDeliver = 1,
                        MemStorage = true,
                        NumReplicas = 1,
                        ReplayPolicy = ConsumerConfigurationReplayPolicy.instant,
                    };

                    if (sequence > 0)
                    {
                        config.DeliverPolicy = ConsumerConfigurationDeliverPolicy.by_start_sequence;
                        config.OptStartSeq = sequence;
                    }

                    Console.WriteLine("xxx new consumer...");
                    var consumer = await _context.CreateConsumerAsync(
                        new ConsumerCreateRequest { StreamName = _stream, Config = config, },
                        cancellationToken: _cancellationToken);

                    Console.WriteLine("xxx new consumer done");
                    Consumer = consumer.Info.Name;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Consumer create loop error: {e}");
            _logger.LogError(e, "Consumer create loop error");
        }
    }

    private void ResetHeartbeatTimer()
    {
        _timer.Change(_hbTimeout, Timeout.Infinite);
    }

    private void StopHeartbeatTimer()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void CreateSub(string origin) => _consumerCreateChannel.Writer.TryWrite(origin);
}
