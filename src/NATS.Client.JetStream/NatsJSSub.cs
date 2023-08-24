using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSSub<TMsg> : NatsSubBase
{
    private readonly ILogger _logger;
    private readonly Channel<NatsJSMsg<TMsg?>> _userMsgs;
    private readonly Channel<ConsumerGetnextRequest> _pullRequests;
    private readonly NatsJSContext _context;
    private readonly string _stream;
    private readonly string _consumer;
    private readonly INatsSerializer _serializer;
    private readonly Timer _timer;
    private readonly Task _pullTask;
    private readonly int _batch;
    private readonly long _expires;
    private readonly long _idle;
    private readonly int _hbTimeout;
    private int _pending;
    private readonly int _thershold;

    public NatsJSSub(
        int batch,
        TimeSpan expires,
        TimeSpan idle,
        NatsJSContext context,
        string stream,
        string consumer,
        string subject,
        NatsSubOpts? opts)
        : base(context.Connection, context.Connection.SubscriptionManager, subject, opts)
    {
        _logger = Connection.Opts.LoggerFactory.CreateLogger<NatsJSSub<TMsg>>();
        _context = context;
        _stream = stream;
        _consumer = consumer;
        _serializer = opts?.Serializer ?? context.Connection.Opts.Serializer;
        _userMsgs = Channel.CreateBounded<NatsJSMsg<TMsg?>>(NatsSub.GetChannelOptions(opts?.ChannelOptions));
        _pullRequests = Channel.CreateBounded<ConsumerGetnextRequest>(NatsSub.GetChannelOptions(opts?.ChannelOptions));

        _batch = batch;
        _thershold = batch / 2;
        _expires = expires.ToNanos();
        _idle = idle.ToNanos();
        _hbTimeout = (int)(idle * 2).TotalMilliseconds;

        Task.Run(PullRequestProcessorLoop);
        Msgs = _userMsgs.Reader;
        _timer = new Timer(
            state =>
            {
                var self = (NatsJSSub<TMsg>)state!;
                self.Pull(_batch);
                ResetPending();
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);

        _pullRequests = Channel.CreateBounded<ConsumerGetnextRequest>(new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _pullTask = Task.Run(PullLoop);
    }

    private void Pull(long batch)
    {
        _pullRequests.Writer.TryWrite(new ConsumerGetnextRequest
        {
            Batch = batch,
            IdleHeartbeat = _idle,
            Expires = _expires,
        });
    }

    private async Task PullLoop()
    {
        await foreach (var pr in _pullRequests.Reader.ReadAllAsync())
        {
            await CallMsgNextAsync(pr);
        }
    }

    public void ResetHeartbeatTimer() => _timer.Change(_hbTimeout, Timeout.Infinite);

    public ChannelReader<NatsJSMsg<TMsg?>> Msgs { get; }

    public ValueTask CallMsgNextAsync(ConsumerGetnextRequest request, CancellationToken cancellationToken = default) =>
        Connection.PubModelAsync(
            subject: $"{_context.Opts.ApiPrefix}.CONSUMER.MSG.NEXT.{_stream}.{_consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: Subject,
            headers: default,
            cancellationToken);

    internal override IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        foreach (var command in base.GetReconnectCommands(sid))
            yield return command;

        var request = ReconnectRequestFactory();

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

    public ConsumerGetnextRequest? ReconnectRequestFactory()
    {
        ResetPending();
        return new ConsumerGetnextRequest
        {
            Batch = _batch,
            IdleHeartbeat = _idle,
            Expires = _expires,
        };
    }

    public void ResetPending()
    {
        _pending = _batch;
    }

    protected override ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        ResetHeartbeatTimer();
        try
        {
            if (subject == Subject)
            {
                if (headersBuffer.HasValue)
                {
                    var headers = new NatsHeaders();
                    if (Connection.HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
                    {
                        if (headers.TryGetValue("Nats-Pending-Messages", out var natsPendingMsgs))
                        {
                            if (int.TryParse(natsPendingMsgs, out var pendingMsgs))
                            {
                                _pending -= pendingMsgs;
                                if (_pending < 0)
                                    _pending = 0;
                            }
                        }

                        if (headers is { Code: 408, Message: NatsHeaders.Messages.RequestTimeout })
                        {
                        }
                        else if (headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat })
                        {
                        }
                        else
                        {
                            Console.WriteLine($"{headers.Dump()}");
                        }
                    }
                    else
                    {
                        _logger.LogError("Can't parse headers: {HeadersBuffer}",
                            Encoding.ASCII.GetString(headersBuffer.Value.ToArray()));
                        throw new NatsJSException("Can't parse headers");
                    }
                }
                else
                {
                    throw new NatsJSException("No header found");
                }

                return ValueTask.CompletedTask;
            }
            else
            {
                var msg = new NatsJSMsg<TMsg?>(NatsMsg<TMsg?>.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    _serializer));

                _pending--;

                return _userMsgs.Writer.WriteAsync(msg);
            }
        }
        finally
        {
            CheckPending();
        }
    }

    public void CheckPending()
    {
        if (_pending <= _thershold)
        {
            Pull(_batch - _pending);
            ResetPending();
        }
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
