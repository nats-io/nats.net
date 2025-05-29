using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

[Flags]
public enum NatsMsgFlags : byte
{
    None = 0,
    Empty = 1,
    NoResponders = 2,
}

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
public readonly record struct NatsMsg<T> : INatsMsg<T>
{
    /*
          2               30
        +--+------------------------------+
        |EN|          Message Size        |
        +--+------------------------------+
        E: Empty flag
        N: No responders flag

        # Size is 30 bits:
        Max Size: 1,073,741,823 (0x3FFFFFFF / 00111111111111111111111111111111)
        Uint.Max: 4,294,967,295
         Int.Max: 2,147,483,647
             8mb:     8,388,608
     */
    private readonly uint _flagsAndSize;

    /// <summary>
    /// NATS message structure as defined by the protocol.
    /// </summary>
    /// <param name="subject">The destination subject to publish to.</param>
    /// <param name="replyTo">The reply subject that subscribers can use to send a response back to the publisher/requester.</param>
    /// <param name="size">Message size in bytes.</param>
    /// <param name="headers">Pass additional information using name-value pairs.</param>
    /// <param name="data">Serializable data object.</param>
    /// <param name="connection">NATS connection this message is associated to.</param>
    /// <param name="flags">Message flags to indicate no responders and empty payloads.</param>
    /// <remarks>
    /// <para>Connection property is used to provide reply functionality.</para>
    /// <para>
    /// Message size is calculated using the same method NATS server uses:
    /// <code lang="C#">
    /// int size = subject.Length + replyTo.Length + headers.Length + payload.Length;
    /// </code>
    /// </para>
    /// </remarks>
    public NatsMsg(
        string subject,
        string? replyTo,
        int size,
        NatsHeaders? headers,
        T? data,
        INatsConnection? connection,
        NatsMsgFlags flags = default)
    {
        Subject = subject;
        ReplyTo = replyTo;
        _flagsAndSize = ((uint)flags << 30) | (uint)(size & 0x3FFFFFFF);
        Headers = headers;
        Data = data;
        Connection = connection;
    }

    /// <inheritdoc />
    public NatsException? Error => Headers?.Error;

    /// <summary>The destination subject to publish to.</summary>
    public string Subject { get; init; }

    /// <summary>The reply subject that subscribers can use to send a response back to the publisher/requester.</summary>
    public string? ReplyTo { get; init; }

    /// <summary>Message size in bytes.</summary>
    public int Size
    {
        // Extract the lower 30 bits
        get => (int)(_flagsAndSize & 0x3FFFFFFF);

        // Clear the lower 30 bits and set the new number
        init
        {
            // Mask the input value to fit within 30 bits (clear upper bits)
            var numberPart = (uint)(value & 0x3FFFFFFF);

            // Clear the lower 30 bits and set the new number value
            // Preserve the flags, update the number
            _flagsAndSize = (_flagsAndSize & 0xC0000000) | numberPart;
        }
    }

    public NatsMsgFlags Flags
    {
        // Extract the two leftmost bits (31st and 30th bit)
        // Mask with 0b11 to get two bits
        get => (NatsMsgFlags)((_flagsAndSize >> 30) & 0b11);

        init
        {
            // Clear the current flag bits (set to 0) and then set the new flag value
            var flagsPart = (uint)value << 30;
            _flagsAndSize = (_flagsAndSize & 0x3FFFFFFF) | flagsPart;
        }
    }

    /// <summary>Pass additional information using name-value pairs.</summary>
    public NatsHeaders? Headers { get; init; }

    /// <summary>Serializable data object.</summary>
    public T? Data { get; init; }

    /// <summary>NATS connection this message is associated to.</summary>
    public INatsConnection? Connection { get; init; }

    public bool IsEmpty => (_flagsAndSize & 0x40000000) != 0;

    public bool HasNoResponders => (_flagsAndSize & 0x80000000) != 0;

    /// <inheritdoc />
    public void EnsureSuccess()
    {
        if (HasNoResponders)
            throw new NatsNoRespondersException();

        if (Error != null)
            throw Error;
    }

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
        return Connection.PublishAsync(ReplyTo, headers, replyTo, opts, cancellationToken);
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
        return Connection.PublishAsync(ReplyTo, data, headers, replyTo, serializer, opts, cancellationToken);
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
        return Connection.PublishAsync(msg with { Subject = ReplyTo }, serializer, opts, cancellationToken);
    }

    public void Deconstruct(out string subject, out string? replyTo, out int size, out NatsHeaders? headers, out T? data, out INatsConnection? connection, out NatsMsgFlags flags)
    {
        subject = Subject;
        replyTo = ReplyTo;
        size = Size;
        headers = Headers;
        data = Data;
        connection = Connection;
        flags = Flags;
    }

    /// <summary>
    /// Builds a new instance of a <see cref="NatsMsg{T}"/> with the specified parameters.
    /// </summary>
    /// <remarks>
    /// (INTERNAL API) This method is intended for internal use only. it doesn't have the same
    /// guarantees as the public API. it may change in future versions with no notice.
    /// </remarks>
    /// <param name="subject">The subject string associated with the message.</param>
    /// <param name="replyTo">The optional reply-to subject string.</param>
    /// <param name="headersBuffer">The optional buffer containing the message headers.</param>
    /// <param name="payloadBuffer">The buffer containing the message payload.</param>
    /// <param name="connection">The connection associated with the message.</param>
    /// <param name="headerParser">The parser for processing message headers.</param>
    /// <param name="serializer">The deserializer for the message payload.</param>
    /// <returns>A new <see cref="NatsMsg{T}"/> instance containing the provided data.</returns>
    /// <exception cref="NatsException">Thrown if there is an error during the processing of the message.</exception>
    public static NatsMsg<T> Build(
        string subject,
        string? replyTo,
        in ReadOnlySequence<byte>? headersBuffer,
        in ReadOnlySequence<byte> payloadBuffer,
        INatsConnection? connection,
        NatsHeaderParser headerParser,
        INatsDeserialize<T> serializer)
    {
        NatsHeaders? headers = null;
        var flags = NatsMsgFlags.None;

        if (payloadBuffer.Length == 0)
        {
            flags |= NatsMsgFlags.Empty;
            if (NatsSubBase.IsHeader503(headersBuffer))
            {
                flags |= NatsMsgFlags.NoResponders;
            }
        }

        if (headersBuffer != null)
        {
            headers = new NatsHeaders();

            try
            {
                // Parsing can also throw an exception.
                if (!headerParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
                {
                    throw new NatsException("Error parsing headers");
                }
            }
            catch (Exception e)
            {
                headers.Error ??= new NatsHeaderParseException(headersBuffer.Value.ToArray(), e);
            }
        }

        headers?.SetReadOnly();

        T? data;
        if (headers?.Error == null)
        {
            try
            {
                data = serializer.Deserialize(payloadBuffer);
            }
            catch (Exception e)
            {
                headers ??= new NatsHeaders();
                headers.Error = new NatsDeserializeException(payloadBuffer.ToArray(), e);
                data = default;
            }
        }
        else
        {
            data = default;
        }

        var size = subject.Length
                   + (replyTo?.Length ?? 0)
                   + (headersBuffer?.Length ?? 0)
                   + payloadBuffer.Length;

        if (Telemetry.HasListeners())
        {
            var activityName = connection is NatsConnection nats
                ? $"{nats.SpanDestinationName(subject)} {Telemetry.Constants.ReceiveActivityName}"
                : Telemetry.Constants.ReceiveActivityName;

            headers ??= new NatsHeaders();

            var activity = Telemetry.StartReceiveActivity(
                connection,
                name: activityName,
                subscriptionSubject: subject,
                queueGroup: null,
                subject: subject,
                replyTo: replyTo,
                bodySize: payloadBuffer.Length,
                size: size,
                headers: headers);

            if (activity is not null)
            {
                headers.Activity = activity;
            }
        }

        return new NatsMsg<T>(subject, replyTo, (int)size, headers, data, connection, flags);
    }

#if NETSTANDARD2_0
#pragma warning disable CS8774 // Member 'ReplyTo' must have a non-null value when exiting..
#endif
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
#if NETSTANDARD2_0
#pragma warning restore CS8774 // Member 'ReplyTo' must have a non-null value when exiting..
#endif
}

/// <summary>
/// Builder class for creating <see cref="NatsMsg{T}" />
/// </summary>
public class NatsMsgBuilder<T>
{
    /// <summary>
    /// The destination subject to publish to.
    /// </summary>
    public string Subject { get; set; } = null!;

    /// <summary>
    /// The reply subject that subscribers can use to send a response back to the publisher/requester.
    /// </summary>
    public string? ReplyTo { get; set; }

    /// <summary>
    /// Pass additional information using name-value pairs.
    /// </summary>
    public NatsHeaders? Headers { get; set; }

    /// <summary>
    ///     Gets or sets the message payload.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// NATS connection this message is associated to.
    /// </summary>
    public INatsConnection? Connection { get; set; }

    /// <summary>
    /// Message flags to indicate no responders and empty payloads.
    /// </summary>
    public NatsMsgFlags Flags { get; set; } = default;

    /// <summary>
    /// Serializer to use for the calculate data size.
    /// </summary>
    /// <remarks>This results in double serialization: once for size calculation, once when publishing.</remarks>
    public INatsSerializer<T>? Serializer { get; set; }

    /// <summary>
    /// The serializer buffer size
    /// </summary>
    /// <remarks>This results in double serialization: once for size calculation, once when publishing.</remarks>
    public int SerializationBufferSize { get; set; } = 256;

    /// <summary>
    /// Encoding used.  Default to utf8 if not provided
    /// </summary>
    public Encoding Encoding { get; set; } = Encoding.UTF8;

    /// <summary>
    ///     Builds and returns the <see cref="NatsMsg{T}" /> instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Subject"/> is null or Empty</exception>
    public NatsMsg<T> Msg
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Subject))
                throw new InvalidOperationException("Subject must be specified and non-empty.");
            var size = 0; // Default size is 0 if not calculated

            switch (Data)
            {
            case ReadOnlyMemory<byte> memoryData:
                size = memoryData.Length;
                break;
            case byte[] byteArray:
                size = byteArray.Length; // Directly use the length of byte[]
                break;
            case string str:
                size = Encoding.GetBytes(str).Length;
                break;
            default:
                if (Serializer != null && Data != null)
                {
                    var bufferWriter = new NatsPooledBufferWriter<byte>(SerializationBufferSize);
                    Serializer.Serialize(bufferWriter, Data);
                    size = bufferWriter.WrittenMemory.Length;
                }

                break;
            }

            if (size > 0)
            {
                size += Subject.Length
                        + (ReplyTo?.Length ?? 0)
                        + (Headers?.GetBytesLength() ?? 0);
            }

            return new NatsMsg<T>(
                Subject,
                ReplyTo,
                size,
                Headers,
                Data,
                Connection,
                Flags);
        }
    }
}

public class NatsDeserializeException : NatsException
{
    public NatsDeserializeException(byte[] rawData, Exception inner)
        : base("Exception during deserialization", inner) =>
        RawData = rawData;

    public byte[] RawData { get; }
}

public class NatsHeaderParseException : NatsException
{
    public NatsHeaderParseException(byte[] rawData, Exception inner)
        : base("Exception parsing headers", inner) =>
        RawData = rawData;

    public byte[] RawData { get; }
}
