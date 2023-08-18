using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSSub<TMsg, TState> : NatsSubBase
{
    private readonly ILogger _logger;
    private readonly Channel<NatsJSMsg<TMsg?>> _userMsgs;
    private readonly Channel<NatsJSControlMsg> _controlMsgs;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly NatsJSSubOpts _consumerOpts;
    private readonly INatsSerializer _serializer;
    private readonly TimeSpan _heartbeat;
    private readonly Func<NatsJSSub<TMsg, TState>, NatsJSControlMsg, Task> _controlHandler;
    private readonly Func<NatsJSSub<TMsg, TState>, ConsumerGetnextRequest?> _reconnectRequestFactory;
    private readonly Timer _heartbeatTimer;

    private int _userMessageCount;

    public NatsJSSub(
        NatsJSContext context,
        string stream,
        string consumer,
        string subject,
        NatsJSSubOpts consumerOpts,
        NatsSubOpts? opts,
        TimeSpan heartbeat,
        Func<NatsJSSub<TMsg, TState>, NatsJSControlMsg, Task> controlHandler,
        Func<NatsJSSub<TMsg, TState>, ConsumerGetnextRequest?> reconnectRequestFactory,
        TState state)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, opts)
    {
        State = state;
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSSub<TMsg, TState>>();
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _consumerOpts = consumerOpts;
        _serializer = opts?.Serializer ?? context.Connection.Opts.Serializer;
        _heartbeat = heartbeat;
        _controlHandler = controlHandler;
        _reconnectRequestFactory = reconnectRequestFactory;
        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg?>>(NatsSub.GetChannelOptions(opts?.ChannelOptions));
        _controlMsgs = Channel.CreateBounded<NatsJSControlMsg>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
        Task.Run(ControlHandlerLoop);

        Msgs = _userMsgs.Reader;

        _heartbeatTimer = new Timer(
            callback: _ => HeartbeatTimerCallback(),
            state: default,
            dueTime: Timeout.Infinite,
            period: Timeout.Infinite);
    }

    public ChannelReader<NatsJSMsg<TMsg?>> Msgs { get; }

    public TState State { get; }

    public ValueTask CallMsgNextAsync(ConsumerGetnextRequest request, CancellationToken cancellationToken = default) =>
        Connection.PubModelAsync(
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: Subject,
            headers: default,
            cancellationToken);

    public void ResetHeartbeatTimer(TimeSpan dueTime) => _heartbeatTimer.Change(dueTime, Timeout.InfiniteTimeSpan);

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        var request = _reconnectRequestFactory(this);

        if (request != null)
        {
            yield return PublishCommand<ConsumerGetnextRequest>.Create(
                pool: Connection.ObjectPool,
                subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
                replyTo: Subject,
                headers: default,
                value: request,
                serializer: JsonNatsSerializer.Default,
                cancellationToken: default);
        }
    }

    protected override ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        ResetHeartbeatTimer(_heartbeat);

        if (subject == Subject)
        {
            // Control messages
            NatsJSControlMsg controlMsg;
            try
            {
                if (headersBuffer.HasValue)
                {
                    var reader = new SequenceReader<byte>(headersBuffer.Value);
                    var headers = new NatsHeaders();
                    if (!Connection.HeaderParser.ParseHeaders(reader, headers))
                    {
                        controlMsg = new NatsJSControlMsg("Error")
                        {
                            Error = new NatsJSControlError("Can't parse headers")
                            {
                                Details = Encoding.ASCII.GetString(headersBuffer.Value.ToArray()),
                            },
                        };
                    }
                    else
                    {
                        controlMsg = new NatsJSControlMsg("Headers") { Headers = headers };
                    }
                }
                else
                {
                    controlMsg = new NatsJSControlMsg("Error") { Error = new NatsJSControlError("No header found") };
                }
            }
            catch (Exception e)
            {
                controlMsg = new NatsJSControlMsg("Error") { Error = new NatsJSControlError(e.Message) { Exception = e } };
            }

            return _controlMsgs.Writer.WriteAsync(controlMsg);
        }

        var msg = new NatsJSMsg<TMsg?>(NatsMsg<TMsg?>.Build(
            subject,
            replyTo,
            headersBuffer,
            payloadBuffer,
            Connection,
            Connection.HeaderParser,
            _serializer));

        var count = Interlocked.Increment(ref _userMessageCount);
        if (count == _consumerOpts.ThreshHoldMaxMsgs)
        {
            _controlMsgs.Writer.TryWrite(NatsJSControlMsg.BatchHighWatermark);
            Interlocked.Exchange(ref _userMessageCount, 0);
        }

        if (count == _consumerOpts.MaxMsgs)
        {
            _controlMsgs.Writer.TryWrite(NatsJSControlMsg.BatchComplete);
            Interlocked.Exchange(ref _userMessageCount, 0);
        }

        return _userMsgs.Writer.WriteAsync(msg);
    }

    protected override void TryComplete()
    {
        _controlMsgs.Writer.TryComplete();
        _userMsgs.Writer.TryComplete();
    }

    private void HeartbeatTimerCallback() =>
        _controlMsgs.Writer.TryWrite(NatsJSControlMsg.HeartBeat);

    private async Task ControlHandlerLoop()
    {
        var reader = _controlMsgs.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var msg))
            {
                try
                {
                    await _controlHandler(this, msg).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Control handler error");
                }
            }
        }
    }
}
