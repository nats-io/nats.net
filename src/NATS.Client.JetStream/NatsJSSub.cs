using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSSub<TMsg, TState> : NatsSubBase, INatsJSSub
where TState : INatsJSSubState
{
    private readonly ILogger _logger;
    private readonly Channel<NatsJSMsg<TMsg?>> _userMsgs;
    private readonly Channel<ConsumerGetnextRequest> _pullRequests;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly INatsSerializer _serializer;

    public NatsJSSub(
        NatsJSContext context,
        string stream,
        string consumer,
        string subject,
        NatsSubOpts? opts,
        TState state)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, opts)
    {
        State = state;
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSSub<TMsg, TState>>();
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _serializer = opts?.Serializer ?? context.Connection.Opts.Serializer;
        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg?>>(NatsSub.GetChannelOptions(opts?.ChannelOptions));
        _pullRequests = Channel.CreateBounded<ConsumerGetnextRequest>(NatsSub.GetChannelOptions(opts?.ChannelOptions));
        Task.Run(PullRequestProcessorLoop);

        Msgs = _userMsgs.Reader;
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

    public override async ValueTask ReadyAsync()
    {
        await base.ReadyAsync();
        await State.ReadyAsync(this);
    }

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        var request = State.ReconnectRequestFactory(this);

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

    protected override async ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        State.ResetHeartbeatTimer(this);

        if (subject == Subject)
        {
            // Control messages
            NatsJSControlMsg controlMsg;
            try
            {
                if (headersBuffer.HasValue)
                {
                    var headers = new NatsHeaders();
                    if (!Connection.HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
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

            await State.ReceivedControlMsgAsync(this, controlMsg);
        }

        var msg = new NatsJSMsg<TMsg?>(NatsMsg<TMsg?>.Build(
            subject,
            replyTo,
            headersBuffer,
            payloadBuffer,
            Connection,
            Connection.HeaderParser,
            _serializer));

        State.ReceivedUserMsg(this);

        await _userMsgs.Writer.WriteAsync(msg);
    }

    protected override void TryComplete()
    {
        _pullRequests.Writer.TryComplete();
        _userMsgs.Writer.TryComplete();
    }

    private async Task PullRequestProcessorLoop()
    {
        var reader = _pullRequests.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var request))
            {
                try
                {
                    await Connection.PubModelAsync(
                        subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
                        data: request,
                        serializer: JsonNatsSerializer.Default,
                        replyTo: Subject,
                        headers: default);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Pull request processor error");
                }
            }
        }
    }
}

public interface INatsJSSub
{
    ValueTask CallMsgNextAsync(ConsumerGetnextRequest request, CancellationToken cancellationToken = default);
}
