using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public sealed class NatsSub<T> : NatsSubBase, INatsSub<T>
{
    private readonly Channel<NatsMsg<T>> _msgs;

    public NatsSub(
        INatsConnection connection,
        INatsSubscriptionManager manager,
        string subject,
        string? queueGroup,
        NatsSubOpts? opts,
        INatsDeserialize<T> serializer,
        CancellationToken cancellationToken = default)
        : base(connection, manager, subject, queueGroup, opts, cancellationToken)
    {
        _msgs = Channel.CreateBounded<NatsMsg<T>>(
            connection.GetBoundedChannelOpts(opts?.ChannelOpts),
            msg => Connection.OnMessageDropped(this, _msgs?.Reader.Count ?? 0, msg));

        Msgs = new ActivityEndingMsgReader<T>(_msgs.Reader, this);

        Serializer = serializer;
    }

    public ChannelReader<NatsMsg<T>> Msgs { get; }

    private INatsDeserialize<T> Serializer { get; }

    protected override async ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        var natsMsg = NatsMsg<T>.Build(
            subject,
            replyTo,
            headersBuffer,
            payloadBuffer,
            Connection,
            Connection.HeaderParser,
            Serializer);

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
