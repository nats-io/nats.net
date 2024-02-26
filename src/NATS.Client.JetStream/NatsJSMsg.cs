using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream;

/// <summary>
/// This interface provides an optional contract when passing
/// messages to processing methods which is usually helpful in
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
public interface INatsJSMsg<out T>
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
    /// The reply subject that subscribers can use to send a response back to the publisher/requester.
    /// </summary>
    public string? ReplyTo { get; }

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
    ValueTask AckAsync(AckOpts? opts = default, CancellationToken cancellationToken = default);

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
    ValueTask NakAsync(AckOpts? opts = default, TimeSpan delay = default, CancellationToken cancellationToken = default);

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
    ValueTask AckProgressAsync(AckOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Instructs the server to stop redelivery of the message without acknowledging it as successfully processed.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    ValueTask AckTerminateAsync(AckOpts? opts = default, CancellationToken cancellationToken = default);
}

/// <summary>
/// NATS JetStream message with <see cref="NatsMsg{T}"/> and control messages.
/// </summary>
/// <typeparam name="T">User message type</typeparam>
public readonly struct NatsJSMsg<T> : INatsJSMsg<T>
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
    /// Activity used to trace the receiving of the this message. It can be used to create child activities under this context.
    /// </summary>
    /// <seealso cref="NatsJSMsgTelemetryExtensions.StartChildActivity{T}"/>
    public Activity? Activity => _msg.Activity;

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

    /// <summary>
    /// The reply subject that subscribers can use to send a response back to the publisher/requester.
    /// </summary>
    public string? ReplyTo => _msg.ReplyTo;

    internal NatsMsg<T> Msg => _msg;

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
    public ValueTask AckAsync(AckOpts? opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.Ack, opts, cancellationToken);

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
    public ValueTask NakAsync(AckOpts? opts = default, TimeSpan delay = default, CancellationToken cancellationToken = default)
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
    public ValueTask AckProgressAsync(AckOpts? opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckProgress, opts, cancellationToken);

    /// <summary>
    /// Instructs the server to stop redelivery of the message without acknowledging it as successfully processed.
    /// </summary>
    /// <param name="opts">Ack options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <returns>A <see cref="ValueTask"/> representing the async call.</returns>
    public ValueTask AckTerminateAsync(AckOpts? opts = default, CancellationToken cancellationToken = default) => SendAckAsync(NatsJSConstants.AckTerminate, opts, cancellationToken);

    private async ValueTask SendAckAsync(ReadOnlySequence<byte> payload, AckOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckPreconditions();
        var activitySource = Activity?.Source ?? Telemetry.NatsInternalActivities;

        if (_msg == default)
            throw new NatsJSException("No user message, can't acknowledge");

        if (opts?.DoubleAck ?? _context.Opts.DoubleAck)
        {
            // TODO: un-hack
            await ((NatsConnection)Connection).RequestAsync<ReadOnlySequence<byte>, object?>(
                activitySource,
                subject: ReplyTo,
                data: payload,
                requestSerializer: NatsRawSerializer<ReadOnlySequence<byte>>.Default,
                replySerializer: NatsRawSerializer<object?>.Default,
                cancellationToken: cancellationToken);
        }
        else
        {
            var sub = ReplyTo;
            if (string.IsNullOrWhiteSpace(sub))
                throw new NatsException("unable to send reply; ReplyTo is empty");

            // TODO: un-hack
            await ((NatsConnection)Connection).PublishAsync(
                activitySource,
                subject: sub,
                data: payload,
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
