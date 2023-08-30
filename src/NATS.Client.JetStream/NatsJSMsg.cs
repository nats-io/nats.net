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
    public NatsJSMsg(NatsMsg<T> msg, NatsJSContext jsContext)
    {
        Msg = msg;
        JSContext = jsContext;
    }

    public NatsJSContext JSContext { get; }

    public NatsMsg<T> Msg { get; }

    public ValueTask AckAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Ack, opts, cancellationToken);

    public ValueTask NackAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Nack, opts, cancellationToken);

    public ValueTask AckProgressAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckProgress, opts, cancellationToken);

    public ValueTask AckTerminateAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckTerminate, opts, cancellationToken);

    private ValueTask SendAckAsync(ReadOnlySequence<byte> payload, AckOpts opts = default, CancellationToken cancellationToken = default)
    {
        if (Msg == default)
            throw new NatsJSException("No user message, can't acknowledge");

        return Msg.ReplyAsync(
            payload: payload,
            opts: new NatsPubOpts
            {
                WaitUntilSent = opts.WaitUntilSent ?? JSContext.Opts.AckOpts.WaitUntilSent,
            },
            cancellationToken: cancellationToken);
    }
}

public readonly record struct AckOpts(bool? WaitUntilSent);
