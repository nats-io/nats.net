using System.Buffers;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.Core.Internal;

namespace NATS.Client.JetStream;

/// <summary>
/// NATS JetStream Subscription with JetStream control message support.
/// </summary>
/// <typeparam name="T">User message type</typeparam>
internal class NatsJSSub<T> : NatsSubBase
{
    private readonly Channel<NatsJSMessage<T?>> _msgs;

    internal NatsJSSub(
        NatsConnection connection,
        ISubscriptionManager manager,
        string subject,
        NatsSubOpts? opts,
        INatsSerializer serializer)
        : base(connection, manager, subject, opts)
    {
        _msgs = Channel.CreateBounded<NatsJSMessage<T?>>(
            NatsSub.GetChannelOptions(opts?.ChannelOptions));

        Serializer = serializer;
    }

    public ChannelReader<NatsJSMessage<T?>> Msgs => _msgs.Reader;

    private INatsSerializer Serializer { get; }

    protected override async ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        if (subject == Subject)
        {
            // TODO: introspect JS control messages
            await _msgs.Writer.WriteAsync(new NatsJSMessage<T?>
            {
                Msg = default,
                ControlMsgType = NatsJSControlMsgType.Heartbeat,
            }).ConfigureAwait(false);
        }
        else
        {
            try
            {
                var msg = NatsMsg<T?>.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    Serializer);

                await _msgs.Writer.WriteAsync(new NatsJSMessage<T?>
                {
                    Msg = msg,
                    ControlMsgType = NatsJSControlMsgType.None,
                }).ConfigureAwait(false);

                DecrementMaxMsgs();
            }
            catch (Exception e)
            {
                var payload = new Memory<byte>(new byte[payloadBuffer.Length]);
                payloadBuffer.CopyTo(payload.Span);

                Memory<byte> headers = default;
                if (headersBuffer != null)
                {
                    headers = new Memory<byte>(new byte[headersBuffer.Value.Length]);
                }

                SetException(new NatsSubException($"Message error: {e.Message}", e, payload, headers));
            }
        }
    }

    protected override void TryComplete() => _msgs.Writer.TryComplete();
}
