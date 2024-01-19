using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
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
    private const int PingNewLineLength = 6;  // "PING\r\n"
    private const int PongNewLineLength = 6;  // "PONG\r\n"
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

    private readonly Encoding _encoding;

    public ProtocolWriter(Encoding encoding) => _encoding = encoding;

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#connect
    // CONNECT {["option_name":option_value],...}
    public void WriteConnect(IBufferWriter<byte> writer, ClientOpts opts)
    {
        var span = writer.GetSpan(UInt64Length);
        BinaryPrimitives.WriteUInt64LittleEndian(span, ConnectSpace);
        writer.Advance(ConnectSpaceLength);

        var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, opts, JsonContext.Default.ClientOpts);

        span = writer.GetSpan(UInt16Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        writer.Advance(NewLineLength);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#ping-pong
    public void WritePing(IBufferWriter<byte> writer)
    {
        var span = writer.GetSpan(UInt64Length);
        BinaryPrimitives.WriteUInt64LittleEndian(span, PingNewLine);
        writer.Advance(PingNewLineLength);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#ping-pong
    public void WritePong(IBufferWriter<byte> writer)
    {
        var span = writer.GetSpan(UInt64Length);
        BinaryPrimitives.WriteUInt64LittleEndian(span, PongNewLine);
        writer.Advance(PongNewLineLength);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#pub
    // PUB <subject> [reply-to] <#bytes>\r\n[payload]\r\n
    // or
    // https://docs.nats.io/reference/reference-protocols/nats-protocol#hpub
    // HPUB <subject> [reply-to] <#header bytes> <#total bytes>\r\n[headers]\r\n\r\n[payload]\r\n
    public void WritePublish(IBufferWriter<byte> writer, string subject, string? replyTo, ReadOnlyMemory<byte>? headers, ReadOnlyMemory<byte> payload)
    {
        // WritePubHpub(writer, subject, replyTo, headers, payload);
        WritePub(writer, subject, replyTo, payload);

        // if (headers == null)
        // {
        //     WritePub(writer, subject, replyTo, payload);
        // }
        // else
        // {
        //     WriteHpub(writer, subject, replyTo, headers, payload);
        // }

        // if (headers == null)
        // {
        //     var span = writer.GetSpan(PubSpaceLength);
        //     BinaryPrimitives.WriteUInt32LittleEndian(span, PubSpace);
        //     writer.Advance(PubSpaceLength);
        // }
        // else
        // {
        //     var span = writer.GetSpan(HpubSpaceLength);
        //     BinaryPrimitives.WriteUInt64LittleEndian(span, HpubSpace);
        //     writer.Advance(HpubSpaceLength);
        // }
        //
        // var headersLength = 0L;
        // var totalLength = 0L;
        // if (headers != null)
        // {
        //     headersLength = headers.Value.Length;
        //     totalLength += headersLength;
        // }
        //
        // totalLength += payload.Length;
        //
        // // subject
        // {
        //     var len = _encoding.GetByteCount(subject) + 1;
        //     var span = writer.GetSpan(len);
        //     var written = _encoding.GetBytes(subject, span);
        //     span[written] = (byte)' ';
        //     writer.Advance(len);
        // }
        //
        // if (replyTo != null)
        // {
        //     var len = _encoding.GetByteCount(replyTo) + 1;
        //     var span = writer.GetSpan(len);
        //     var written = _encoding.GetBytes(replyTo, span);
        //     span[written] = (byte)' ';
        //     writer.Advance(len);
        // }
        //
        // if (headers != null)
        // {
        //     var span = writer.GetSpan(MaxIntStringLength + 1);
        //     if (!Utf8Formatter.TryFormat(headersLength, span, out var written))
        //     {
        //         ThrowOnUtf8FormatFail();
        //     }
        //
        //     span[written] = (byte)' ';
        //     writer.Advance(written + 1);
        // }
        //
        // // payload length
        // {
        //     var span = writer.GetSpan(MaxIntStringLength);
        //     if (!Utf8Formatter.TryFormat(totalLength, span, out var written))
        //     {
        //         ThrowOnUtf8FormatFail();
        //     }
        //
        //     writer.Advance(written);
        // }
        //
        // // CRLF
        // {
        //     var span = writer.GetSpan(UInt16Length);
        //     BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        //     writer.Advance(NewLineLength);
        // }
        //
        // if (headers != null)
        // {
        //     writer.Write(headers.Value.Span);
        // }
        //
        // writer.Write(payload.Span);
        //
        // // CRLF
        // {
        //     var span = writer.GetSpan(UInt16Length);
        //     BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        //     writer.Advance(NewLineLength);
        // }
    }

     public void WritePubHpub(IBufferWriter<byte> writer, string subject, string? replyTo, ReadOnlyMemory<byte>? headers, ReadOnlyMemory<byte> payload)
    {
        if (headers == null)
        {
            var span = writer.GetSpan(PubSpaceLength);
            BinaryPrimitives.WriteUInt32LittleEndian(span, PubSpace);
            writer.Advance(PubSpaceLength);
        }
        else
        {
            var span = writer.GetSpan(HpubSpaceLength);
            BinaryPrimitives.WriteUInt64LittleEndian(span, HpubSpace);
            writer.Advance(HpubSpaceLength);
        }

        var headersLength = 0L;
        var totalLength = 0L;
        if (headers != null)
        {
            headersLength = headers.Value.Length;
            totalLength += headersLength;
        }

        totalLength += payload.Length;

        // subject
        {
            var len = _encoding.GetByteCount(subject) + 1;
            var span = writer.GetSpan(len);
            var written = _encoding.GetBytes(subject, span);
            span[written] = (byte)' ';
            writer.Advance(len);
        }

        if (replyTo != null)
        {
            var len = _encoding.GetByteCount(replyTo) + 1;
            var span = writer.GetSpan(len);
            var written = _encoding.GetBytes(replyTo, span);
            span[written] = (byte)' ';
            writer.Advance(len);
        }

        if (headers != null)
        {
            var span = writer.GetSpan(MaxIntStringLength + 1);
            if (!Utf8Formatter.TryFormat(headersLength, span, out var written))
            {
                ThrowOnUtf8FormatFail();
            }

            span[written] = (byte)' ';
            writer.Advance(written + 1);
        }

        // payload length
        {
            var span = writer.GetSpan(MaxIntStringLength);
            if (!Utf8Formatter.TryFormat(totalLength, span, out var written))
            {
                ThrowOnUtf8FormatFail();
            }

            writer.Advance(written);
        }

        // CRLF
        {
            var span = writer.GetSpan(UInt16Length);
            BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
            writer.Advance(NewLineLength);
        }

        if (headers != null)
        {
            writer.Write(headers.Value.Span);
        }

        writer.Write(payload.Span);

        // CRLF
        {
            var span = writer.GetSpan(UInt16Length);
            BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
            writer.Advance(NewLineLength);
        }
    }

    public void WritePub(IBufferWriter<byte> writer, string subject, string? replyTo, ReadOnlyMemory<byte> payload)
    {
        // PUB <subject> [reply-to] <#bytes>\r\n[payload]\r\n

        Span<byte> span1 = stackalloc byte[MaxIntStringLength];
        {
            if (!Utf8Formatter.TryFormat(payload.Length, span1, out var written))
            {
                ThrowOnUtf8FormatFail();
            }

            span1 = span1.Slice(0, written);
        }

        var total = PubSpaceLength;

        var subjectSpaceLength = _encoding.GetByteCount(subject) + 1;
        total += subjectSpaceLength;

        var replyToLengthSpace = 0;
        if (replyTo != null)
        {
            replyToLengthSpace = _encoding.GetByteCount(replyTo) + 1;
            total += replyToLengthSpace;
        }

        total += span1.Length + NewLineLength + payload.Length + NewLineLength;

        var span = writer.GetSpan(total);

        BinaryPrimitives.WriteUInt32LittleEndian(span, PubSpace);
        span = span.Slice(PubSpaceLength);

        _encoding.GetBytes(subject, span);
        span[subjectSpaceLength - 1] = (byte)' ';
        span = span.Slice(subjectSpaceLength);

        if (replyTo != null)
        {
            _encoding.GetBytes(replyTo, span);
            span[replyToLengthSpace - 1] = (byte)' ';
            span = span.Slice(replyToLengthSpace);
        }

        span1.CopyTo(span);
        span = span.Slice(span1.Length);

        BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
        span = span.Slice(NewLineLength);

        payload.Span.CopyTo(span);
        span = span.Slice(payload.Length);

        BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);

        writer.Advance(total);

        /*
        {
            var span2 = writer.GetSpan(PubSpaceLength);
            BinaryPrimitives.WriteUInt32LittleEndian(span2, PubSpace);
            writer.Advance(PubSpaceLength);
        }

        var headersLength = 0L;
        var totalLength = 0L;

        totalLength += payload.Length;

        // subject
        {
            var len = _encoding.GetByteCount(subject) + 1;
            var span2 = writer.GetSpan(len);
            var written = _encoding.GetBytes(subject, span2);
            span[written] = (byte)' ';
            writer.Advance(len);
        }

        if (replyTo != null)
        {
            var len = _encoding.GetByteCount(replyTo) + 1;
            var span2 = writer.GetSpan(len);
            var written = _encoding.GetBytes(replyTo, span2);
            span[written] = (byte)' ';
            writer.Advance(len);
        }

        // payload length
        {
            var span2 = writer.GetSpan(MaxIntStringLength);
            if (!Utf8Formatter.TryFormat(totalLength, span2, out var written))
            {
                ThrowOnUtf8FormatFail();
            }

            writer.Advance(written);
        }

        // CRLF
        {
            var span2 = writer.GetSpan(UInt16Length);
            BinaryPrimitives.WriteUInt16LittleEndian(span2, NewLine);
            writer.Advance(NewLineLength);
        }

        writer.Write(payload.Span);

        // CRLF
        {
            var span2 = writer.GetSpan(UInt16Length);
            BinaryPrimitives.WriteUInt16LittleEndian(span2, NewLine);
            writer.Advance(NewLineLength);
        }
        */
    }

    public void WriteHpub(IBufferWriter<byte> writer, string subject, string? replyTo, ReadOnlyMemory<byte>? headers, ReadOnlyMemory<byte> payload)
    {
        if (headers == null)
        {
            var span = writer.GetSpan(PubSpaceLength);
            BinaryPrimitives.WriteUInt32LittleEndian(span, PubSpace);
            writer.Advance(PubSpaceLength);
        }
        else
        {
            var span = writer.GetSpan(HpubSpaceLength);
            BinaryPrimitives.WriteUInt64LittleEndian(span, HpubSpace);
            writer.Advance(HpubSpaceLength);
        }

        var headersLength = 0L;
        var totalLength = 0L;
        if (headers != null)
        {
            headersLength = headers.Value.Length;
            totalLength += headersLength;
        }

        totalLength += payload.Length;

        // subject
        {
            var len = _encoding.GetByteCount(subject) + 1;
            var span = writer.GetSpan(len);
            var written = _encoding.GetBytes(subject, span);
            span[written] = (byte)' ';
            writer.Advance(len);
        }

        if (replyTo != null)
        {
            var len = _encoding.GetByteCount(replyTo) + 1;
            var span = writer.GetSpan(len);
            var written = _encoding.GetBytes(replyTo, span);
            span[written] = (byte)' ';
            writer.Advance(len);
        }

        if (headers != null)
        {
            var span = writer.GetSpan(MaxIntStringLength + 1);
            if (!Utf8Formatter.TryFormat(headersLength, span, out var written))
            {
                ThrowOnUtf8FormatFail();
            }

            span[written] = (byte)' ';
            writer.Advance(written + 1);
        }

        // payload length
        {
            var span = writer.GetSpan(MaxIntStringLength);
            if (!Utf8Formatter.TryFormat(totalLength, span, out var written))
            {
                ThrowOnUtf8FormatFail();
            }

            writer.Advance(written);
        }

        // CRLF
        {
            var span = writer.GetSpan(UInt16Length);
            BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
            writer.Advance(NewLineLength);
        }

        if (headers != null)
        {
            writer.Write(headers.Value.Span);
        }

        writer.Write(payload.Span);

        // CRLF
        {
            var span = writer.GetSpan(UInt16Length);
            BinaryPrimitives.WriteUInt16LittleEndian(span, NewLine);
            writer.Advance(NewLineLength);
        }
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#sub
    // SUB <subject> [queue group] <sid>
    public void WriteSubscribe(IBufferWriter<byte> writer, int sid, string subject, string? queueGroup, int? maxMsgs)
    {
        // 'SUB '                       + subject                                +' '+ sid                +'\r\n'
        var ctrlLen = SubSpaceLength + _encoding.GetByteCount(subject) + 1 + MaxIntStringLength + NewLineLength;

        if (queueGroup != null)
        {
            // len  += queueGroup                                +' '
            ctrlLen += _encoding.GetByteCount(queueGroup) + 1;
        }

        var span = writer.GetSpan(ctrlLen);
        BinaryPrimitives.WriteUInt32LittleEndian(span, SubSpace);
        var size = SubSpaceLength;
        span = span[SubSpaceLength..];

        var written = _encoding.GetBytes(subject, span);
        span[written] = (byte)' ';
        size += written + 1;
        span = span[(written + 1)..];

        if (queueGroup != null)
        {
            written = _encoding.GetBytes(subject, span);
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

        writer.Advance(size);

        // Immediately send UNSUB <sid> <max-msgs> to minimize the risk of
        // receiving more messages than <max-msgs> in case they are published
        // between our SUB and UNSUB calls.
        if (maxMsgs != null)
        {
            WriteUnsubscribe(writer, sid, maxMsgs);
        }
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#unsub
    // UNSUB <sid> [max_msgs]
    public void WriteUnsubscribe(IBufferWriter<byte> writer, int sid, int? maxMessages)
    {
        // 'UNSUB '                       + sid                +'\r\n'
        var ctrlLen = UnsubSpaceLength + MaxIntStringLength + NewLineLength;
        if (maxMessages != null)
        {
            // len  +=' '+ max_msgs
            ctrlLen += 1 + MaxIntStringLength;
        }

        var span = writer.GetSpan(ctrlLen);
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

        writer.Advance(size);
    }

    // optimization detailed here: https://github.com/nats-io/nats.net.v2/issues/320#issuecomment-1886165748
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOnUtf8FormatFail() => throw new NatsException("Can not format integer.");
}
