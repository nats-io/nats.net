using System.Buffers;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream;

/// <summary>
/// NATS JetStream message with <see cref="NatsMsg{T}"/> and control messages.
/// </summary>
/// <typeparam name="T">User message type</typeparam>
public readonly struct NatsJSMsg<T>
{
    public NatsJSMsg(NatsMsg<T> msg) => Msg = msg;

    public NatsMsg<T> Msg { get; }

    public ValueTask AckAsync(CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Ack, cancellationToken);

    public ValueTask NackAsync(CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Nack, cancellationToken);

    public ValueTask AckProgressAsync(CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckProgress, cancellationToken);

    public ValueTask AckTerminateAsync(CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckTerminate, cancellationToken);

    private ValueTask SendAckAsync(ReadOnlySequence<byte> payload, CancellationToken cancellationToken = default)
    {
        if (Msg == default)
            throw new NatsJSException("No user message, can't acknowledge");

        return Msg.ReplyAsync(
            payload: payload,
            opts: new NatsPubOpts { WaitUntilSent = true, },
            cancellationToken: cancellationToken);
    }
}
