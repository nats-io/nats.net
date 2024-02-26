using System.Buffers;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public sealed class NatsSub<T> : NatsSubBase, INatsSub<T>
{
    private readonly ActivitySource _activitySource;
    private readonly Channel<NatsMsg<T>> _msgs;

    internal NatsSub(
        ActivitySource activitySource,
        NatsConnection connection,
        ISubscriptionManager manager,
        string subject,
        string? queueGroup,
        NatsSubOpts? opts,
        INatsDeserialize<T> serializer,
        CancellationToken cancellationToken = default)
        : base(connection, manager, subject, queueGroup, opts, cancellationToken)
    {
        _activitySource = activitySource;
        _msgs = Channel.CreateBounded<NatsMsg<T>>(
            connection.GetChannelOpts(connection.Opts, opts?.ChannelOpts),
            msg => Connection.OnMessageDropped(this, _msgs?.Reader.Count ?? 0, msg));

        Msgs = new ActivityEndingMsgReader<T>(_msgs.Reader);

        Serializer = serializer;
    }

    public ChannelReader<NatsMsg<T>> Msgs { get; }

    private INatsDeserialize<T> Serializer { get; }

    protected override async ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        var natsMsg = ParseMsg(
            activitySource: _activitySource,
            activityName: Telemetry.Constants.ReceiveActivityName,
            subject: subject,
            replyTo: replyTo,
            headersBuffer,
            in payloadBuffer,
            Connection,
            Connection.HeaderParser,
            serializer: Serializer);

        await _msgs.Writer.WriteAsync(natsMsg).ConfigureAwait(false);

        DecrementMaxMsgs();
    }

    protected override void TryComplete() => _msgs.Writer.TryComplete();
}

public class NatsSubException : NatsException
{
    public NatsSubException(string message, ExceptionDispatchInfo exception, Memory<byte> payload, Memory<byte> headers)
        : base(message)
    {
        Exception = exception;
        Payload = payload;
        Headers = headers;
    }

    public ExceptionDispatchInfo Exception { get; }

    public Memory<byte> Payload { get; }

    public Memory<byte> Headers { get; }
}
