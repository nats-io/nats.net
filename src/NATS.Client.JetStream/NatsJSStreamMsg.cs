using System.Buffers;
using System.Text;
using Microsoft.Extensions.Primitives;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// A message retrieved from a JetStream stream, encapsulating the message data and metadata.
/// </summary>
/// <typeparam name="T">The type of the message data.</typeparam>
public readonly record struct NatsStreamMsg<T>(
    T? Data,
    ulong Sequence,
    string Subject,
    DateTimeOffset Time,
    NatsHeaders? Headers)
{
    private const string NatsSequenceHeader = "Nats-Sequence";
    private const string NatsTimeStampHeader = "Nats-Time-Stamp";
    private const string NatsSubjectHeader = "Nats-Subject";

    /// <summary>
    /// Creates a <see cref="NatsStreamMsg{T}"/> from a <see cref="NatsMsg{T}"/> returned by a direct get.
    /// </summary>
    /// <param name="msg">The message returned by the direct get API.</param>
    /// <returns>A <see cref="NatsStreamMsg{T}"/> containing the message data and metadata.</returns>
    /// <exception cref="ArgumentNullException">The <paramref name="msg"/> is null.</exception>
    /// <exception cref="NatsJSNoMessageFoundException">The message was not found (404).</exception>
    public static NatsStreamMsg<T> FromDirect(NatsMsg<T> msg)
    {
        if (EqualityComparer<NatsMsg<T>>.Default.Equals(msg, default))
        {
            throw new ArgumentNullException(nameof(msg));
        }

        if (msg.Headers is { Code: 404 })
        {
            throw new NatsJSNoMessageFoundException();
        }

        var subject = msg.Subject;
        var sequence = 0UL;
        var time = default(DateTimeOffset);

        if (msg.Headers is { } headers)
        {
            if (headers.TryGetLastValue(NatsSubjectHeader, out var subjectFromHeaders))
            {
                subject = subjectFromHeaders;
            }

            var sequenceStringFromHeaders = headers[NatsSequenceHeader];
            if (!StringValues.IsNullOrEmpty(sequenceStringFromHeaders) && ulong.TryParse(sequenceStringFromHeaders, out var sequenceFromHeaders))
            {
                sequence = sequenceFromHeaders;
            }

            var timeStringFromHeaders = headers[NatsTimeStampHeader];
            if (!StringValues.IsNullOrEmpty(timeStringFromHeaders) && DateTimeOffset.TryParse(timeStringFromHeaders, out var timeFromHeaders))
            {
                time = timeFromHeaders;
            }
        }

        return new NatsStreamMsg<T>(msg.Data, sequence, subject, time, msg.Headers);
    }

    /// <summary>
    /// Creates a <see cref="NatsStreamMsg{T}"/> from a <see cref="StreamMsgGetResponse"/> returned by the stream get API.
    /// </summary>
    /// <param name="response">The response from the stream get API.</param>
    /// <param name="serializer">The deserializer to use for the message data.</param>
    /// <returns>A <see cref="NatsStreamMsg{T}"/> containing the message data and metadata.</returns>
    /// <exception cref="ArgumentNullException">The <paramref name="response"/> or <paramref name="serializer"/> is null.</exception>
    public static NatsStreamMsg<T> FromStreamResponse(StreamMsgGetResponse response, INatsDeserialize<T> serializer)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        var message = response.Message;
        var data = message.Data.IsEmpty ? default : serializer.Deserialize(new ReadOnlySequence<byte>(message.Data), new NatsMsgContext(message.Subject));
        var headers = message.Hdrs is { Length: > 0 } ? ParseHeaders(message.Hdrs) : null;

        return new NatsStreamMsg<T>(data, message.Seq, message.Subject, message.Time, headers);
    }

    private static NatsHeaders? ParseHeaders(string hdrs)
    {
        if (string.IsNullOrEmpty(hdrs))
        {
            return null;
        }

        var bytes = Convert.FromBase64String(hdrs);
        var parser = new NatsHeaderParser(Encoding.UTF8);
        var headers = new NatsHeaders();
        if (parser.ParseHeaders(new SequenceReader<byte>(new ReadOnlySequence<byte>(bytes)), headers))
        {
            return headers;
        }

        return null;
    }
}
