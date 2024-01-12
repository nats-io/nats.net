using System.Buffers.Binary;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class ProtocolWriter
{
    private const int MaxIntStringLength = 9; // https://github.com/nats-io/nats-server/blob/28a2a1000045b79927ebf6b75eecc19c1b9f1548/server/util.go#L85C8-L85C23
    private const int NewLineLength = 2; // "\r\n"
    private const int PubSpaceLength = 4; // "PUB "
    private const int SubSpaceLength = 4; // "SUB "
    private const int ConnectSpaceLength = 8;  // "CONNECT "
    private const int HpubSpaceLength = 5;  // "HPUB "
    private const int PingNewLineLength = 6;  // "PING "
    private const int PongNewLineLength = 6;  // "PONG "
    private const int UnsubSpaceLength = 6;  // "UNSUB "
    private const int UInt16Length = 2;
    private const int UInt64Length = 8;

    // 2 bytes, make sure string length is 2
    private static readonly ushort NewLine = BinaryPrimitives.ReadUInt16LittleEndian("\r\n"u8);

    // 4 bytes, make sure string length is 4
    private static readonly uint PubSpace = BinaryPrimitives.ReadUInt32LittleEndian("PUB "u8);
    private static readonly uint SubSpace = BinaryPrimitives.ReadUInt32LittleEndian("SUB "u8);

    // 8 bytes, make sure string length is 8
    private static readonly ulong ConnectSpace = BinaryPrimitives.ReadUInt64LittleEndian("CONNECT "u8);
    private static readonly ulong HpubSpace = BinaryPrimitives.ReadUInt64LittleEndian("HPUB    "u8);
    private static readonly ulong PingNewLine = BinaryPrimitives.ReadUInt64LittleEndian("PING\r\n  "u8);
    private static readonly ulong PongNewLine = BinaryPrimitives.ReadUInt64LittleEndian("PONG\r\n  "u8);
    private static readonly ulong UnsubSpace = BinaryPrimitives.ReadUInt64LittleEndian("UNSUB   "u8);

    private readonly PipeWriter _writer;
    private readonly HeaderWriter _headerWriter;
    private readonly Encoding _subjectEncoding;

    public ProtocolWriter(PipeWriter writer, Encoding subjectEncoding, Encoding headerEncoding)
    {
        _writer = writer;
        _subjectEncoding = subjectEncoding;
        _headerWriter = new HeaderWriter(writer, headerEncoding);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#connect
    // CONNECT {["option_name":option_value],...}
    public void WriteConnect(ClientOpts opts)
    {
        var span = _writer.GetSpan(UInt64Length);
        BinaryPrimitives.WriteUInt64LittleEndian(span, ConnectSpace);
        _writer.Advance(ConnectSpaceLength);

        var jsonWriter = new Utf8JsonWriter(_writer);
        JsonSerializer.Serialize(jsonWriter, opts, JsonContext.Default.ClientOpts);

        span = _writer.GetSpan(UInt16Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        _writer.Advance(NewLineLength);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#ping-pong
    public void WritePing()
    {
        var span = _writer.GetSpan(UInt64Length);
        BinaryPrimitives.WriteUInt64LittleEndian(span, PingNewLine);
        _writer.Advance(PingNewLineLength);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#ping-pong
    public void WritePong()
    {
        var span = _writer.GetSpan(UInt64Length);
        BinaryPrimitives.WriteUInt64LittleEndian(span, PongNewLine);
        _writer.Advance(PongNewLineLength);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#pub
    // PUB <subject> [reply-to] <#bytes>\r\n[payload]\r\n
    // or
    // https://docs.nats.io/reference/reference-protocols/nats-protocol#hpub
    // HPUB <subject> [reply-to] <#header bytes> <#total bytes>\r\n[headers]\r\n\r\n[payload]\r\n
    //
    // returns the number of bytes that should be skipped when writing to the wire
    public int WritePublish<T>(string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer)
    {
        int ctrlLen;
        if (headers == null)
        {
            // 'PUB '   + subject                                +' '+ payload len        +'\r\n'
            ctrlLen = PubSpaceLength + _subjectEncoding.GetByteCount(subject) + 1 + MaxIntStringLength + NewLineLength;
        }
        else
        {
            // 'HPUB '  + subject                                +' '+ header len         +' '+ payload len        +'\r\n'
            ctrlLen = HpubSpaceLength + _subjectEncoding.GetByteCount(subject) + 1 + MaxIntStringLength + 1 + MaxIntStringLength + NewLineLength;
        }

        if (replyTo != null)
        {
            // len  += replyTo                                +' '
            ctrlLen += _subjectEncoding.GetByteCount(replyTo) + 1;
        }

        var ctrlSpan = _writer.GetSpan(ctrlLen);
        var span = ctrlSpan;
        if (headers == null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span, PubSpace);
            span = span[PubSpaceLength..];
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span, HpubSpace);
            span = span[HpubSpaceLength..];
        }

        var written = _subjectEncoding.GetBytes(subject, span);
        span[written] = (byte)' ';
        span = span[(written + 1)..];

        if (replyTo != null)
        {
            written = _subjectEncoding.GetBytes(replyTo, span);
            span[written] = (byte)' ';
            span = span[(written + 1)..];
        }

        Span<byte> lenSpan;
        if (headers == null)
        {
            // len =            payload len
            lenSpan = span[..MaxIntStringLength];
            span = span[lenSpan.Length..];
        }
        else
        {
            // len =          header len         +' '+ payload len
            lenSpan = span[..(MaxIntStringLength + 1 + MaxIntStringLength)];
            span = span[lenSpan.Length..];
        }

        BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        _writer.Advance(ctrlLen);

        var headersLength = 0L;
        var totalLength = 0L;
        if (headers != null)
        {
            headersLength = _headerWriter.Write(headers);
            totalLength += headersLength;
        }

        // Consider null as empty payload. This way we are able to transmit null values as sentinels.
        // Another point is serializer behaviour. For instance JSON serializer seems to serialize null
        // as a string "null", others might throw exception.
        if (value != null)
        {
            var initialCount = _writer.UnflushedBytes;
            serializer.Serialize(_writer, value);
            totalLength += _writer.UnflushedBytes - initialCount;
        }

        span = _writer.GetSpan(UInt16Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        _writer.Advance(NewLineLength);

        // write the length
        var lenWritten = 0;
        if (headers != null)
        {
            if (!Utf8Formatter.TryFormat(headersLength, lenSpan, out lenWritten))
            {
                ThrowOnUtf8FormatFail();
            }

            lenSpan[lenWritten] = (byte)' ';
            lenWritten += 1;
        }

        if (!Utf8Formatter.TryFormat(totalLength, lenSpan[lenWritten..], out var tLen))
        {
            ThrowOnUtf8FormatFail();
        }

        lenWritten += tLen;
        var trim = lenSpan.Length - lenWritten;
        if (trim > 0)
        {
            // shift right
            ctrlSpan[..(ctrlLen - trim - NewLineLength)].CopyTo(ctrlSpan[trim..]);
            ctrlSpan[..trim].Clear();
        }

        return trim;
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#sub
    // SUB <subject> [queue group] <sid>
    public void WriteSubscribe(int sid, string subject, string? queueGroup, int? maxMsgs)
    {
        // 'SUB '                       + subject                                +' '+ sid                +'\r\n'
        var ctrlLen = SubSpaceLength + _subjectEncoding.GetByteCount(subject) + 1 + MaxIntStringLength + NewLineLength;

        if (queueGroup != null)
        {
            // len  += queueGroup                                +' '
            ctrlLen += _subjectEncoding.GetByteCount(queueGroup) + 1;
        }

        var span = _writer.GetSpan(ctrlLen);
        BinaryPrimitives.WriteUInt32LittleEndian(span, SubSpace);
        var size = SubSpaceLength;
        span = span[SubSpaceLength..];

        var written = _subjectEncoding.GetBytes(subject, span);
        span[written] = (byte)' ';
        size += written + 1;
        span = span[(written + 1)..];

        if (queueGroup != null)
        {
            written = _subjectEncoding.GetBytes(subject, span);
            span[written] = (byte)' ';
            size += written + 1;
            span = span[(written + 1)..];
        }

        if (!Utf8Formatter.TryFormat(sid, span, out written))
        {
            ThrowOnUtf8FormatFail();
        }

        size += written;
        span = span[written..];

        BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        size += NewLineLength;

        _writer.Advance(size);

        // Immediately send UNSUB <sid> <max-msgs> to minimize the risk of
        // receiving more messages than <max-msgs> in case they are published
        // between our SUB and UNSUB calls.
        if (maxMsgs != null)
        {
            WriteUnsubscribe(sid, maxMsgs);
        }
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#unsub
    // UNSUB <sid> [max_msgs]
    public void WriteUnsubscribe(int sid, int? maxMessages)
    {
        // 'UNSUB '                       + sid                +'\r\n'
        var ctrlLen = UnsubSpaceLength + MaxIntStringLength + NewLineLength;
        if (maxMessages != null)
        {
            // len  +=' '+ max_msgs
            ctrlLen += 1 + MaxIntStringLength;
        }

        var span = _writer.GetSpan(ctrlLen);
        BinaryPrimitives.WriteUInt64LittleEndian(span, UnsubSpace);
        var size = UnsubSpaceLength;
        span = span[UnsubSpaceLength..];

        if (!Utf8Formatter.TryFormat(sid, span, out var written))
        {
            ThrowOnUtf8FormatFail();
        }

        size += written;
        span = span[written..];
        if (maxMessages != null)
        {
            span[0] = (byte)' ';
            if (!Utf8Formatter.TryFormat(maxMessages.Value, span[1..], out written))
            {
                ThrowOnUtf8FormatFail();
            }

            size += written + 1;
            span = span[(written + 1)..];
        }

        BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        size += NewLineLength;

        _writer.Advance(size);
    }

    // optimization detailed here: https://github.com/nats-io/nats.net.v2/issues/320#issuecomment-1886165748
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOnUtf8FormatFail() => throw new NatsException("Can not format integer.");
}
