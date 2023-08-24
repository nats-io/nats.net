using NATS.Client.Core;
using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream;

/// <summary>
/// NATS JetStream message with <see cref="NatsMsg"/> and control messages.
/// </summary>
public readonly struct NatsJSMsg
{
    public NatsMsg Msg { get; init; }

    public ValueTask Ack(CancellationToken cancellationToken = default)
    {
        if (Msg == default)
            throw new NatsJSException("No user message, can't acknowledge");
        return Msg.ReplyAsync(NatsJSConstants.Ack, cancellationToken: cancellationToken);
    }
}

/// <summary>
/// NATS JetStream message with <see cref="NatsMsg{T}"/> and control messages.
/// </summary>
/// <typeparam name="T">User message type</typeparam>
public readonly struct NatsJSMsg<T>
{
    public NatsJSMsg(NatsMsg<T> msg) => Msg = msg;

    public NatsMsg<T> Msg { get; }

    public ValueTask AckAsync(CancellationToken cancellationToken = default)
    {
        if (Msg == default)
            throw new NatsJSException("No user message, can't acknowledge");
        return Msg.ReplyAsync(NatsJSConstants.Ack, opts: new NatsPubOpts
        {
            WaitUntilSent = true,
        }, cancellationToken: cancellationToken);
    }
}
