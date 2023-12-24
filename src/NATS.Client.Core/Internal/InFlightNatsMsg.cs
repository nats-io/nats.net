using System.Buffers;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

public readonly struct InFlightNatsMsg<T>
{
    public static bool operator ==(InFlightNatsMsg<T> left, InFlightNatsMsg<T> right) => left.Equals(right);

    public static bool operator !=(InFlightNatsMsg<T> left, InFlightNatsMsg<T> right) => !left.Equals(right);

    public bool Equals(InFlightNatsMsg<T> other) =>
        Subject == other.Subject
        && Equals(_otherData, other._otherData)
        && EqualityComparer<T?>.Default.Equals(Data, other.Data)
        && Size == other.Size;

    public override bool Equals(object? obj) => obj is InFlightNatsMsg<T> other && Equals(other);


    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = Subject.GetHashCode();
            hashCode = (hashCode * 397) ^ (_otherData != null ? _otherData.GetHashCode() : 0);
            hashCode = (hashCode * 397) ^ EqualityComparer<T?>.Default.GetHashCode(Data);
            hashCode = (hashCode * 397) ^ Size;
            return hashCode;
        }
    }

    public readonly string Subject;
    private readonly object? _otherData;
    public readonly T? Data;
    public readonly int Size;
    private readonly int _headerType;

    public InFlightNatsMsg(string subject, string? replyTo, int size, NatsHeaders? headers, T? data)
    {
        Subject = subject;
        if (headers == null)
        {
            _otherData = replyTo;
            _headerType = replyTo == null ? 1 : 0;
        }
        else
        {
            headers.ReplyTo = replyTo;
            _otherData = headers;
            _headerType = 2;
        }

        Data = data;
        Size = size;
    }

    public InFlightNatsMsg(string subject, int size, object? otherData, T? data, int dataType)
    {
        Subject = subject;
        Size = size;
        _otherData = otherData;
        _headerType = dataType;
        Data = data;
    }

    public string? ReplyTo => _headerType == 2 ? (_otherData as NatsHeaders)?.ReplyTo : _otherData as string;

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
        T? data = default;
        int size = subject.Length;
        if (payloadBuffer.Length > 0)
        {
            size = size + (int)payloadBuffer.Length;
            data = serializer.Deserialize(payloadBuffer);
        }

        NatsHeaders? headers = null;

        int _dataType = 0;
        if (headersBuffer != null)
        {
            headers = new NatsHeaders();
            if (!headerParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
            {
                throw new NatsException("Error parsing headers");
            }

            size = size + (int)headersBuffer.Value.Length;
            headers.SetReadOnly();
            _dataType = 2;
        }

        if (replyTo != null)
        {
            size = size + replyTo.Length;
            _dataType = _dataType + 1;
        }

        //return new InFlightNatsMsg<T>(subject, replyTo, (int) size, headers, data);
        return new InFlightNatsMsg<T>(subject, size, headers, data, _dataType);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NatsMsg<T> ToNatsMsg(INatsConnection? connection)
    {
        switch (_headerType)
        {
            case 0:
                return new NatsMsg<T>(Subject, null, Size, null, Data, connection);
            case 1:
                return new NatsMsg<T>(Subject, _otherData as string, Size, null, Data, connection);
            case 2:
                var h = _otherData as NatsHeaders;
                return new NatsMsg<T>(Subject, h.ReplyTo, Size, h, Data, connection);
            default:
                return new NatsMsg<T>(Subject, _otherData as string, Size, null, Data, connection);

        }
    }
}
