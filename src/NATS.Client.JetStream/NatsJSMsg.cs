using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream;

public interface INatsJSMsg<T>
{
    /// <summary>
    /// Subject of the user message.
    /// </summary>
    string Subject { get; }

    /// <summary>
    /// Message size in bytes.
    /// </summary>
    /// <remarks>
    /// Message size is calculated using the same method NATS server uses:
    /// <code lang="C#">
    /// int size = subject.Length + replyTo.Length + headers.Length + payload.Length;
    /// </code>
    /// </remarks>
    int Size { get; }

    /// <summary>
    /// Headers of the user message if set.
    /// </summary>
    NatsHeaders? Headers { get; }

    /// <summary>
    /// Deserialized user data.
    /// </summary>
    T? Data { get; }

    /// <summary>
    /// The connection messages was delivered on.
    /// </summary>
    INatsConnection? Connection { get; }

    /// <summary>
    /// Additional metadata about the message.
    /// </summary>
    NatsJSMsgMetadata? Metadata { get; }

    /// <summary>
    /// Reply with an empty message.
    /// </summary>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask ReplyAsync(NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges the message was completely handled.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    ValueTask AckAsync(AckOpts opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that the message will not be processed now and processing can move onto the next message.
    /// </summary>
    /// <param name="delay">Delay redelivery of the message.</param>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    /// <remarks>
    /// Messages rejected using <c>-NAK</c> will be resent by the NATS JetStream server after the configured timeout
    /// or the delay parameter if it's specified.
    /// </remarks>
    ValueTask NakAsync(AckOpts opts = default, TimeSpan delay = default, CancellationToken cancellationToken = default);

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
    ValueTask AckProgressAsync(AckOpts opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Instructs the server to stop redelivery of the message without acknowledging it as successfully processed.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    ValueTask AckTerminateAsync(AckOpts opts = default, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options to be used when acknowledging messages received from a stream using a consumer.
/// </summary>
/// <param name="WaitUntilSent">Wait for the publish to be flushed down to the network.</param>
/// <param name="DoubleAck">Ask server for an acknowledgment.</param>
public readonly record struct AckOpts(bool? WaitUntilSent = false, bool? DoubleAck = false);

/// <summary>
/// NATS JetStream message with <see cref="NatsMsg{T}"/> and control messages.
/// </summary>
/// <typeparam name="T">User message type</typeparam>
public class NatsJSMsg<T> : INatsJSMsg<T>
{
    private readonly NatsJSContext _context;
    private readonly NatsMsg<T> _msg;
    private readonly Lazy<NatsJSMsgMetadata?> _replyToDateTimeAndSeq;

    public NatsJSMsg(NatsMsg<T> msg, NatsJSContext context)
    {
        _msg = msg;
        _context = context;
        _replyToDateTimeAndSeq = new Lazy<NatsJSMsgMetadata?>(() => ReplyToDateTimeAndSeq.Parse(msg.ReplyTo));
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
    /// Additional metadata about the message.
    /// </summary>
    public NatsJSMsgMetadata? Metadata => _replyToDateTimeAndSeq.Value;

    private string? ReplyTo => _msg.ReplyTo;

    /// <summary>
    /// Reply with an empty message.
    /// </summary>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    public ValueTask ReplyAsync(NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        _msg.ReplyAsync(headers, replyTo, opts, cancellationToken);

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
    /// <param name="delay">Delay redelivery of the message.</param>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    /// <remarks>
    /// Messages rejected using <c>-NAK</c> will be resent by the NATS JetStream server after the configured timeout
    /// or the delay parameter if it's specified.
    /// </remarks>
    public ValueTask NakAsync(AckOpts opts = default, TimeSpan delay = default, CancellationToken cancellationToken = default)
    {
        if (delay == default)
        {
            return SendAckAsync(NatsJSConstants.Nak, opts, cancellationToken);
        }
        else
        {
            var nakDelayed = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes($"-NAK {{\"delay\": {delay.ToNanos()}}}"));
            return SendAckAsync(nakDelayed, opts, cancellationToken);
        }
    }

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

    private async ValueTask SendAckAsync(ReadOnlySequence<byte> payload, AckOpts opts = default, CancellationToken cancellationToken = default)
    {
        CheckPreconditions();

        if (_msg == default)
            throw new NatsJSException("No user message, can't acknowledge");

        if ((opts.DoubleAck ?? _context.Opts.AckOpts.DoubleAck) == true)
        {
            await Connection.RequestAsync<ReadOnlySequence<byte>, object?>(
                subject: ReplyTo,
                data: payload,
                requestSerializer: NatsRawSerializer<ReadOnlySequence<byte>>.Default,
                replySerializer: NatsRawSerializer<object?>.Default,
                cancellationToken: cancellationToken);
        }
        else
        {
            await _msg.ReplyAsync(
                data: payload,
                opts: new NatsPubOpts
                {
                    WaitUntilSent = opts.WaitUntilSent ?? _context.Opts.AckOpts.WaitUntilSent,
                },
                serializer: NatsRawSerializer<ReadOnlySequence<byte>>.Default,
                cancellationToken: cancellationToken);
        }
    }

    [MemberNotNull(nameof(Connection))]
    [MemberNotNull(nameof(ReplyTo))]
    private void CheckPreconditions()
    {
        if (Connection == default)
        {
            throw new NatsException("unable to send acknowledgment; message did not originate from a consumer");
        }

        if (string.IsNullOrWhiteSpace(ReplyTo))
        {
            throw new NatsException("unable to send acknowledgment; ReplyTo is empty");
        }
    }
}
