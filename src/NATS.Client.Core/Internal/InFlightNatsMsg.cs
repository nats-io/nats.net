using System.Buffers;

namespace NATS.Client.Core.Internal;

internal readonly record struct InFlightNatsMsg<T>
{
    public readonly string Subject;
    private readonly object? _otherData;
    public readonly T? Data;
    public readonly int Size;

    public InFlightNatsMsg(string subject, string? replyTo, int size, NatsHeaders? headers, T? data)
    {
        Subject = subject;
        if (headers == null)
        {
            _otherData = replyTo;
        }
        else
        {
            _otherData = headers.ReplyTo = replyTo;
        }

        Data = data;
        Size = size;
    }

    public string? ReplyTo => _otherData is NatsHeaders nh ? nh.ReplyTo : _otherData as string;

    public NatsHeaders? Headers => _otherData as NatsHeaders;

    internal static InFlightNatsMsg<T> BuildInternal(
        string subject,
        string? replyTo,
        in ReadOnlySequence<byte>? headersBuffer,
        in ReadOnlySequence<byte> payloadBuffer,
        INatsConnection? connection,
        NatsHeaderParser headerParser,
        INatsDeserialize<T> serializer)
    {
        // Consider an empty payload as null or default value for value types. This way we are able to
        // receive sentinels as nulls or default values. This might cause an issue with where we are not
        // able to differentiate between an empty sentinel and actual default value of a struct e.g. 0 (zero).
        var data = payloadBuffer.Length > 0
            ? serializer.Deserialize(payloadBuffer)
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

        return new InFlightNatsMsg<T>(subject, replyTo, (int)size, headers, data);
    }

    public NatsMsg<T> ToNatsMsg(INatsConnection? connection)
    {
        if (_otherData is NatsHeaders nh)
        {
            return new NatsMsg<T>(Subject, nh.ReplyTo, Size, nh, Data, connection);
        }
        else
        {
            return new NatsMsg<T>(Subject, _otherData as string, Size, null, Data, connection);
        }
    }
}
