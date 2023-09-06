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
    private readonly NatsJSContext _context;
    private readonly NatsMsg<T> _msg;

    internal NatsJSMsg(NatsMsg<T> msg, NatsJSContext context)
    {
        _msg = msg;
        _context = context;
    }

    public string Subject => _msg.Subject;

    public int Size => _msg.Size;

    public NatsHeaders? Headers => _msg.Headers;

    public T? Data => _msg.Data;

    public INatsConnection? Connection => _msg.Connection;

    public ValueTask AckAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Ack, opts, cancellationToken);

    public ValueTask NackAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Nack, opts, cancellationToken);

    public ValueTask AckProgressAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckProgress, opts, cancellationToken);

    public ValueTask AckTerminateAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckTerminate, opts, cancellationToken);

    private ValueTask SendAckAsync(ReadOnlySequence<byte> payload, AckOpts opts = default, CancellationToken cancellationToken = default)
    {
        if (_msg == default)
            throw new NatsJSException("No user message, can't acknowledge");

        return _msg.ReplyAsync(
            payload: payload,
            opts: new NatsPubOpts
            {
                WaitUntilSent = opts.WaitUntilSent ?? _context.Opts.AckOpts.WaitUntilSent,
            },
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Options to be used when acknowledging messages received from a stream using a consumer.
/// </summary>
/// <param name="WaitUntilSent">Wait for the publish to be flushed down to the network.</param>
public readonly record struct AckOpts(bool? WaitUntilSent);
