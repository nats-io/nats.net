using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace NATS.Client.Core;

/// <summary>
/// NATS message structure as defined by the protocol.
/// </summary>
/// <param name="Subject">The destination subject to publish to.</param>
/// <param name="ReplyTo">The reply subject that subscribers can use to send a response back to the publisher/requester.</param>
/// <param name="Size">Message size in bytes.</param>
/// <param name="Headers">Pass additional information using name-value pairs.</param>
/// <param name="Data">The message payload data.</param>
/// <param name="Connection">NATS connection this message is associated to.</param>
/// <remarks>
/// <para>Connection property is used to provide reply functionality.</para>
/// <para>
/// Message size is calculated using the same method NATS server uses:
/// <code lang="C#">
/// int size = subject.Length + replyTo.Length + headers.Length + payload.Length;
/// </code>
/// </para>
/// </remarks>
public readonly record struct NatsMsg(
    string Subject,
    string? ReplyTo,
    int Size,
    NatsHeaders? Headers,
    ReadOnlyMemory<byte> Data,
    INatsConnection? Connection)
{
    internal static NatsMsg Build(
        string subject,
        string? replyTo,
        in ReadOnlySequence<byte>? headersBuffer,
        in ReadOnlySequence<byte> payloadBuffer,
        INatsConnection? connection,
        NatsHeaderParser headerParser)
    {
        NatsHeaders? headers = null;

        if (headersBuffer != null)
        {
            headers = new NatsHeaders();
            if (!headerParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
            {
                throw new NatsException("Error parsing headers");
            }

            headers.SetReadOnly();
        }

        var size = subject.Length
                   + (replyTo?.Length ?? 0)
                   + (headersBuffer?.Length ?? 0)
                   + payloadBuffer.Length;

        return new NatsMsg(subject, replyTo, (int)size, headers, payloadBuffer.ToArray(), connection);
    }

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="payload">The message payload data.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </remarks>
    public ValueTask ReplyAsync(ReadOnlySequence<byte> payload = default, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(ReplyTo!, payload, headers, replyTo, opts, cancellationToken);
    }

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg"/> representing message details.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </remarks>
    public ValueTask ReplyAsync(NatsMsg msg, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(msg with { Subject = ReplyTo! }, opts, cancellationToken);
    }

    [MemberNotNull(nameof(Connection))]
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
    INatsConnection? Connection)
{
    internal static NatsMsg<T> Build(
        string subject,
        string? replyTo,
        in ReadOnlySequence<byte>? headersBuffer,
        in ReadOnlySequence<byte> payloadBuffer,
        INatsConnection? connection,
        NatsHeaderParser headerParser,
        INatsSerializer serializer)
    {
        // Consider an empty payload as null or default value for value types. This way we are able to
        // receive sentinels as nulls or default values. This might cause an issue with where we are not
        // able to differentiate between an empty sentinel and actual default value of a struct e.g. 0 (zero).
        var data = payloadBuffer.Length > 0
            ? serializer.Deserialize<T>(payloadBuffer)
            : default;

        NatsHeaders? headers = null;

        if (headersBuffer != null)
        {
            headers = new NatsHeaders();
            if (!headerParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
            {
                throw new NatsException("Error parsing headers");
            }

            headers.SetReadOnly();
        }

        var size = subject.Length
            + (replyTo?.Length ?? 0)
            + (headersBuffer?.Length ?? 0)
            + payloadBuffer.Length;

        return new NatsMsg<T>(subject, replyTo, (int)size, headers, data, connection);
    }

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="data">Serializable data object.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="TReply">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </remarks>
    public ValueTask ReplyAsync<TReply>(TReply data, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(ReplyTo!, data, headers, replyTo, opts, cancellationToken);
    }

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg"/> representing message details.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="TReply">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </remarks>
    public ValueTask ReplyAsync<TReply>(NatsMsg<TReply> msg, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(msg with { Subject = ReplyTo! }, opts, cancellationToken);
    }

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="payload">The message payload data.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </remarks>
    public ValueTask ReplyAsync(ReadOnlySequence<byte> payload = default, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(ReplyTo!, payload: payload, headers, replyTo, opts, cancellationToken);
    }

    /// <summary>
    /// Reply to this message.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg"/> representing message details.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishes a new message using the reply-to subject from the this message as the destination subject.
    /// </remarks>
    public ValueTask ReplyAsync(NatsMsg msg, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(msg with { Subject = ReplyTo! }, opts, cancellationToken);
    }

    [MemberNotNull(nameof(Connection))]
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
