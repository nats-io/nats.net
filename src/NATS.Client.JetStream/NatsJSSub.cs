using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSSub<T> : NatsSubBase
{
    private readonly ILogger _logger;
    private readonly Channel<NatsJSMsg<T?>> _userMsgs;
    private readonly Channel<NatsJSControlMsg> _controlMsgs;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly INatsSerializer _serializer;
    private readonly TimeSpan _heartbeat;
    private readonly Func<NatsJSSub<T>, NatsJSControlMsg, Task> _controlHandler;
    private readonly Func<ConsumerGetnextRequest?> _reconnectRequestFactory;
    private readonly Timer _heartbeatTimer;

    public NatsJSSub(
        NatsJSContext context,
        string stream,
        string consumer,
        string subject,
        NatsSubOpts? opts,
        TimeSpan heartbeat,
        Func<NatsJSSub<T>, NatsJSControlMsg, Task> controlHandler,
        Func<ConsumerGetnextRequest?> reconnectRequestFactory)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, opts)
    {
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSSub<T>>();
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _serializer = opts?.Serializer ?? context.Connection.Opts.Serializer;
        _heartbeat = heartbeat;
        _controlHandler = controlHandler;
        _reconnectRequestFactory = reconnectRequestFactory;
        _userMsgs = Channel.CreateBounded<NatsJSMsg<T?>>(NatsSub.GetChannelOptions(opts?.ChannelOptions));
        _controlMsgs = Channel.CreateBounded<NatsJSControlMsg>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
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

    private void ResetHeartbeatTimer(TimeSpan dueTime) => _heartbeatTimer.Change(dueTime, Timeout.InfiniteTimeSpan);

    public ValueTask CallMsgNextAsync(ConsumerGetnextRequest request, CancellationToken cancellationToken = default) =>
        Connection.PubModelAsync(
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: Subject,
            headers: default,
            cancellationToken);

    private void HeartbeatTimerCallback() =>
        _controlMsgs.Writer.TryWrite(NatsJSControlMsg.HeartBeat);

    public ChannelReader<NatsJSMsg<T?>> Msgs { get; }

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        var request = _reconnectRequestFactory();

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
                        controlMsg = new NatsJSControlMsg
                        {
                            Error = new NatsJSControlError("Can't parse headers")
                            {
                                Details = Encoding.ASCII.GetString(headersBuffer.Value.ToArray()),
                            },
                        };
                    }
                    else
                    {
                        controlMsg = new NatsJSControlMsg { Headers = headers };
                    }
                }
                else
                {
                    controlMsg = new NatsJSControlMsg { Error = new NatsJSControlError("No header found") };
                }
            }
            catch (Exception e)
            {
                controlMsg = new NatsJSControlMsg { Error = new NatsJSControlError(e.Message) { Exception = e } };
            }

            return _controlMsgs.Writer.WriteAsync(controlMsg);
        }

        var msg = new NatsJSMsg<T?>(NatsMsg<T?>.Build(
            subject,
            replyTo,
            headersBuffer,
            payloadBuffer,
            Connection,
            Connection.HeaderParser,
            _serializer));

        return _userMsgs.Writer.WriteAsync(msg);
    }

    protected override void TryComplete()
    {
        _controlMsgs.Writer.TryComplete();
        _userMsgs.Writer.TryComplete();
    }

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

public class NatsJSControlMsg
{
    public static readonly NatsJSControlMsg HeartBeat = new();

    public NatsHeaders? Headers { get; init; }

    public NatsJSControlError? Error { get; init; }
}

public enum NatsJSControlErrorType
{
    Internal,
    Server,
}

public class NatsJSControlError
{
    public NatsJSControlError(string error) => Error = error;

    public NatsJSControlErrorType Type { get; init; } = NatsJSControlErrorType.Internal;

    public string Error { get; }

    public Exception? Exception { get; init; }

    public string Details { get; init; } = string.Empty;
}
