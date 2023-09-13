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

    /// <summary>
    /// Subject of the user message.
    /// </summary>
    public string Subject => _msg.Subject;

    /// <summary>
    /// Message size in bytes.
    /// </summary>
    /// <remarks>
    /// Message size is calculated using the same method NATS server uses:
    /// <code lang="C#">
    /// int size = subject.Length + replyTo.Length + headers.Length + payload.Length;
    /// </code>
    /// </remarks>
    public int Size => _msg.Size;

    /// <summary>
    /// Headers of the user message if set.
    /// </summary>
    public NatsHeaders? Headers => _msg.Headers;

    /// <summary>
    /// Deserialized user data.
    /// </summary>
    public T? Data => _msg.Data;

    /// <summary>
    /// The connection messages was delivered on.
    /// </summary>
    public INatsConnection? Connection => _msg.Connection;

    /// <summary>
    /// Acknowledges the message was completely handled.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    public ValueTask AckAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Ack, opts, cancellationToken);

    /// <summary>
    /// Signals that the message will not be processed now and processing can move onto the next message.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    /// <remarks>
    /// Messages rejected using <c>NACK</c> will be resent by the NATS JetStream server after the configured timeout.
    /// </remarks>
    public ValueTask NackAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Nack, opts, cancellationToken);

    /// <summary>
    /// Indicates that work is ongoing and the wait period should be extended.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    /// <remarks>
    /// <para>
    /// Time period is defined by the consumer's <c>ack_wait</c> configuration on the server which is
    /// defined as how long to allow messages to remain un-acknowledged before attempting redelivery.
    /// </para>
    /// <para>
    /// This message must be sent before the <c>ack_wait</c> period elapses. The period should be extended
    /// by another amount of time equal to <c>ack_wait</c> by the NATS JetStream server.
    /// </para>
    /// </remarks>
    public ValueTask AckProgressAsync(AckOpts opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckProgress, opts, cancellationToken);

    /// <summary>
    /// Instructs the server to stop redelivery of the message without acknowledging it as successfully processed.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
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
