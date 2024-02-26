using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NATS.Client.Core;

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
/// <see cref="NatsMsg{T}"/> is a value type and boxing is required.
/// A boxing allocation occurs when a value type is converted to the
/// interface type. This is because the interface type is a reference
/// type and the value type must be converted to a reference type.
/// You should benchmark your application to determine if the
/// interface is worth the performance penalty or makes any noticeable
/// degradation in performance.
/// </para>
/// </remarks>
/// <typeparam name="T">Data type of the payload</typeparam>
public interface INatsMsg<T>
{
    /// <summary>The destination subject to publish to.</summary>
    string Subject { get; init; }

    /// <summary>The reply subject that subscribers can use to send a response back to the publisher/requester.</summary>
    string? ReplyTo { get; init; }

    /// <summary>Message size in bytes.</summary>
    int Size { get; init; }

    /// <summary>Pass additional information using name-value pairs.</summary>
    NatsHeaders? Headers { get; init; }

    /// <summary>Serializable data object.</summary>
    T? Data { get; init; }

    /// <summary>NATS connection this message is associated to.</summary>
    INatsConnection? Connection { get; init; }

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
    /// Reply to this message.
    /// </summary>
    /// <param name="data">Serializable data object.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="TReply">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// <para>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </para>
    /// <para>
    /// If the <paramref name="serializer"/> is not specified, the <see cref="INatsSerializerRegistry"/> assigned to
    /// the <see cref="NatsConnection"/> will be used to find a serializer for the type <typeparamref name="TReply"/>.
    /// You can specify a <see cref="INatsSerializerRegistry"/> in <see cref="NatsOpts"/> when creating a
    /// <see cref="NatsConnection"/>. If not specified, <see cref="NatsDefaultSerializerRegistry"/> will be used.
    /// </para>
    /// </remarks>
    ValueTask ReplyAsync<TReply>(TReply data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<TReply>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg{T}"/> representing message details.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="TReply">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </remarks>
    ValueTask ReplyAsync<TReply>(NatsMsg<TReply> msg, INatsSerialize<TReply>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);
}

/// <summary>
/// NATS message structure as defined by the protocol.
/// </summary>
/// <param name="Subject">The destination subject to publish to.</param>
/// <param name="ReplyTo">The reply subject that subscribers can use to send a response back to the publisher/requester.</param>
/// <param name="Size">Message size in bytes.</param>
/// <param name="Headers">Pass additional information using name-value pairs.</param>
/// <param name="Data">Serializable data object.</param>
/// <param name="Connection">NATS connection this message is associated to.</param>
/// <typeparam name="T">Specifies the type of data that may be sent to the NATS Server.</typeparam>
/// <remarks>
/// <para>Connection property is used to provide reply functionality.</para>
/// <para>
/// Message size is calculated using the same method NATS server uses:
/// <code lang="C#">
/// int size = subject.Length + replyTo.Length + headers.Length + payload.Length;
/// </code>
/// </para>
/// </remarks>
public readonly record struct NatsMsg<T>(
    string Subject,
    string? ReplyTo,
    int Size,
    NatsHeaders? Headers,
    T? Data,
    INatsConnection? Connection) : INatsMsg<T>
{
    /// <summary>
    /// Activity used to trace the receiving of the this message. It can be used to create child activities under this context.
    /// </summary>
    /// <seealso cref="NatsMsgTelemetryExtensions.StartChildActivity{T}"/>
    public Activity? Activity => Headers?.Activity;

    /// <summary>
    /// Reply with an empty message.
    /// </summary>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    public ValueTask ReplyAsync(NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        var activitySource = Activity?.Source ?? Telemetry.NatsInternalActivities;

        // TODO: un-hack
        return ((NatsConnection)Connection).PublishNoneAsync(activitySource, subject: ReplyTo, headers, replyTo, cancellationToken);
    }

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="data">Serializable data object.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="TReply">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// <para>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </para>
    /// <para>
    /// If the <paramref name="serializer"/> is not specified, the <see cref="INatsSerializerRegistry"/> assigned to
    /// the <see cref="NatsConnection"/> will be used to find a serializer for the type <typeparamref name="TReply"/>.
    /// You can specify a <see cref="INatsSerializerRegistry"/> in <see cref="NatsOpts"/> when creating a
    /// <see cref="NatsConnection"/>. If not specified, <see cref="NatsDefaultSerializerRegistry"/> will be used.
    /// </para>
    /// </remarks>
    public ValueTask ReplyAsync<TReply>(TReply data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<TReply>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        var activitySource = Activity?.Source ?? Telemetry.NatsInternalActivities;

        // TODO: un-hack
        return ((NatsConnection)Connection).PublishAsync(activitySource, subject: ReplyTo, data, headers, replyTo, serializer, cancellationToken);
    }

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg{T}"/> representing message details.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="TReply">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </remarks>
    public ValueTask ReplyAsync<TReply>(NatsMsg<TReply> msg, INatsSerialize<TReply>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        var activitySource = Activity?.Source ?? Telemetry.NatsInternalActivities;

        // TODO: un-hack
        return ((NatsConnection)Connection).PublishAsync(activitySource, subject: ReplyTo, msg.Data, msg.Headers, msg.ReplyTo, serializer, cancellationToken);
    }

    [MemberNotNull(nameof(Connection))]
    [MemberNotNull(nameof(ReplyTo))]
    private void CheckReplyPreconditions()
    {
        if (Connection == default)
        {
            throw new NatsException("unable to send reply; message did not originate from a subscription");
        }

        if (string.IsNullOrWhiteSpace(ReplyTo))
        {
            throw new NatsException("unable to send reply; ReplyTo is empty");
        }
    }
}
