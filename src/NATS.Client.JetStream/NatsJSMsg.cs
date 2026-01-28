using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream;

/// <summary>
/// This interface provides an optional contract when passing
/// messages to processing methods, which is usually helpful in
/// creating test doubles in unit testing.
/// </summary>
/// <remarks>
/// <para>
/// Using this interface is optional and should not affect functionality.
/// </para>
/// <para>
/// There is a performance penalty when using this interface because
/// <see cref="NatsJSMsg{T}"/> is a value type and boxing is required.
/// A boxing allocation occurs when a value type is converted to the
/// interface type. This is because the interface type is a reference
/// type and the value type must be converted to a reference type.
/// You should benchmark your application to determine if the
/// interface is worth the performance penalty or makes any noticeable
/// degradation in performance.
/// </para>
/// </remarks>
/// <typeparam name="T">Data type of the payload</typeparam>
public interface INatsJSMsg<out T> : INatsMsg
{
    /// <summary>
    /// Get subject of the user message.
    /// </summary>
    string Subject { get; }

    /// <summary>
    /// Get message size in bytes.
    /// </summary>
    /// <remarks>
    /// Message size is calculated using the same method NATS server uses:
    /// <code lang="C#">
    /// int size = subject.Length + replyTo.Length + headers.Length + payload.Length;
    /// </code>
    /// </remarks>
    int Size { get; }

    /// <summary>
    /// Get deserialized user data.
    /// </summary>
    T? Data { get; }

    /// <summary>
    /// Get the connection messages were delivered on.
    /// </summary>
    INatsConnection? Connection { get; }

    /// <summary>
    /// Additional metadata about the message.
    /// </summary>
    NatsJSMsgMetadata? Metadata { get; }

    /// <summary>
    /// The reply subject that subscribers can use to send a response back to the publisher/requester.
    /// </summary>
    public string? ReplyTo { get; }

    /// <summary>Any errors (generally serialization errors) encountered while processing the message.</summary>
    NatsException? Error { get; }

    /// <summary>Throws an exception if the message contains any errors (generally serialization errors).</summary>
    void EnsureSuccess();

    /// <summary>
    /// Reply with an empty message.
    /// </summary>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    [Obsolete("ReplyAsync is not valid when using JetStream. The reply message will never reach the Requestor as NATS will reply to all JetStream publish by default.")]
    ValueTask ReplyAsync(NatsHeaders? headers = null, string? replyTo = null, NatsPubOpts? opts = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges the message was completely handled.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    ValueTask AckAsync(AckOpts? opts = null, CancellationToken cancellationToken = default);

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
    ValueTask NakAsync(AckOpts? opts = null, TimeSpan delay = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates that work is ongoing and the wait period should be extended.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    /// <remarks>
    /// <para>
    /// Time period is defined by the consumer's <c>ack_wait</c> configuration on the server which is
    /// defined as how long to allow messages to remain unacknowledged before attempting redelivery.
    /// </para>
    /// <para>
    /// This message must be sent before the <c>ack_wait</c> period elapses. The period should be extended
    /// by another amount of time equal to <c>ack_wait</c> by the NATS JetStream server.
    /// </para>
    /// </remarks>
    ValueTask AckProgressAsync(AckOpts? opts = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Instructs the server to stop redelivery of the message without acknowledging it as successfully processed.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    ValueTask AckTerminateAsync(AckOpts? opts = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Instructs the server to stop redelivery of the message without acknowledging it as successfully processed.
    /// </summary>
    /// <param name="reason">Optional reason for termination, included in JetStream advisory events. Requires NATS Server 2.10.4+.</param>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    ValueTask AckTerminateAsync(string reason, AckOpts? opts = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// NATS JetStream message with <see cref="NatsMsg{T}"/> and control messages.
/// </summary>
/// <typeparam name="T">User message type</typeparam>
public readonly struct NatsJSMsg<T> : INatsJSMsg<T>
{
    private readonly INatsJSContext _context;
    private readonly NatsMsg<T> _msg;
    private readonly Lazy<NatsJSMsgMetadata?> _replyToDateTimeAndSeq;

    public NatsJSMsg(NatsMsg<T> msg, INatsJSContext context)
    {
        _msg = msg;
        _context = context;
        _replyToDateTimeAndSeq = new Lazy<NatsJSMsgMetadata?>(() => ReplyToDateTimeAndSeq.Parse(msg.ReplyTo));
    }

    /// <inheritdoc />
    public string Subject => _msg.Subject;

    /// <inheritdoc />
    public int Size => _msg.Size;

    /// <inheritdoc />
    public NatsHeaders? Headers => _msg.Headers;

    /// <inheritdoc />
    public T? Data => _msg.Data;

    /// <inheritdoc />
    public INatsConnection? Connection => _msg.Connection;

    /// <inheritdoc />
    public NatsJSMsgMetadata? Metadata => _replyToDateTimeAndSeq.Value;

    /// <inheritdoc />
    public string? ReplyTo => _msg.ReplyTo;

    /// <inheritdoc />
    public NatsException? Error => _msg.Error;

    internal NatsMsg<T> Msg => _msg;

    /// <inheritdoc />
    public void EnsureSuccess() => _msg.EnsureSuccess();

    /// <inheritdoc />
    [Obsolete("ReplyAsync is not valid when using JetStream. The reply message will never reach the Requestor as NATS will reply to all JetStream publish by default.")]
    public ValueTask ReplyAsync(NatsHeaders? headers = null, string? replyTo = null, NatsPubOpts? opts = null, CancellationToken cancellationToken = default) =>
        _msg.ReplyAsync(headers, replyTo, opts, cancellationToken);

    /// <inheritdoc />
    public ValueTask AckAsync(AckOpts? opts = null, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Ack, opts, cancellationToken);

    /// <inheritdoc />
    public ValueTask NakAsync(AckOpts? opts = null, TimeSpan delay = default, CancellationToken cancellationToken = default)
    {
        if (delay == TimeSpan.Zero)
        {
            return SendAckAsync(NatsJSConstants.Nak, opts, cancellationToken);
        }
        else
        {
            var nakDelayed = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes($"-NAK {{\"delay\": {delay.ToNanos()}}}"));
            return SendAckAsync(nakDelayed, opts, cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask AckProgressAsync(AckOpts? opts = null, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckProgress, opts, cancellationToken);

    /// <inheritdoc />
    public ValueTask AckTerminateAsync(AckOpts? opts = null, CancellationToken cancellationToken = default)
        => AckTerminateInternalAsync(opts, null, cancellationToken);

    /// <inheritdoc />
    public ValueTask AckTerminateAsync(string reason, AckOpts? opts = null, CancellationToken cancellationToken = default)
        => AckTerminateInternalAsync(opts, reason, cancellationToken);

    private ValueTask AckTerminateInternalAsync(AckOpts? opts, string? reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return SendAckAsync(NatsJSConstants.AckTerminate, opts, cancellationToken);
        }

        return AckTerminateWithReasonAsync(reason!, opts, cancellationToken);
    }

    private async ValueTask AckTerminateWithReasonAsync(string reason, AckOpts? opts, CancellationToken cancellationToken)
    {
        var reasonByteCount = Encoding.ASCII.GetByteCount(reason);
        var totalLength = 6 + reasonByteCount; // `+TERM ` is 6 bytes

        var buffer = ArrayPool<byte>.Shared.Rent(totalLength);
        try
        {
#if NETSTANDARD2_0
            Buffer.BlockCopy(NatsJSMsgConstants.TermPrefix, 0, buffer, 0, 6);
            Encoding.ASCII.GetBytes(reason, 0, reason.Length, buffer, 6);
#else
            "+TERM "u8.CopyTo(buffer.AsSpan());
            Encoding.ASCII.GetBytes(reason.AsSpan(), buffer.AsSpan(6));
#endif

            await SendAckAsync(new ReadOnlySequence<byte>(buffer, 0, totalLength), opts, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask SendAckAsync(ReadOnlySequence<byte> payload, AckOpts? opts = null, CancellationToken cancellationToken = default)
    {
        CheckPreconditions();

        if (_msg == default)
            throw new NatsJSException("No user message, can't acknowledge");

        if (opts?.DoubleAck ?? _context.Opts.DoubleAck)
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
                serializer: NatsRawSerializer<ReadOnlySequence<byte>>.Default,
                cancellationToken: cancellationToken);
        }
    }

#if NETSTANDARD2_0
#pragma warning disable CS8774 // Member 'ReplyTo' must have a non-null value when exiting..
#endif
    [MemberNotNull(nameof(Connection))]
    [MemberNotNull(nameof(ReplyTo))]
    private void CheckPreconditions()
    {
        if (Connection == null)
        {
            throw new NatsException("unable to send acknowledgment; message did not originate from a consumer");
        }

        if (string.IsNullOrWhiteSpace(ReplyTo))
        {
            throw new NatsException("unable to send acknowledgment; ReplyTo is empty");
        }
    }
}
#if NETSTANDARD2_0
#pragma warning restore CS8774 // Member 'ReplyTo' must have a non-null value when exiting..
#endif

/// <summary>
/// Options to be used when acknowledging messages received from a stream using a consumer.
/// </summary>
public readonly record struct AckOpts
{
    /// <summary>
    /// Ask server for an acknowledgment
    /// </summary>
    public bool? DoubleAck { get; init; }
}

#if NETSTANDARD2_0
internal static class NatsJSMsgConstants
{
    internal static readonly byte[] TermPrefix = "+TERM "u8.ToArray();
}
#endif
