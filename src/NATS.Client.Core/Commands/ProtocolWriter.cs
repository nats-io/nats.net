using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class ProtocolWriter
{
    private const int MaxIntStringLength = 9; // https://github.com/nats-io/nats-server/blob/28a2a1000045b79927ebf6b75eecc19c1b9f1548/server/util.go#L85C8-L85C23
    private const int NewLineLength = 2; // \r\n

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
        WriteConstant(CommandConstants.ConnectWithPadding);

        var jsonWriter = new Utf8JsonWriter(_writer);
        JsonSerializer.Serialize(jsonWriter, opts, JsonContext.Default.ClientOpts);

        WriteConstant(CommandConstants.NewLine);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#ping-pong
    public void WritePing()
    {
        WriteConstant(CommandConstants.PingNewLine);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#ping-pong
    public void WritePong()
    {
        WriteConstant(CommandConstants.PongNewLine);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#pub
    // PUB <subject> [reply-to] <#bytes>\r\n[payload]\r\n
    // or
    // https://docs.nats.io/reference/reference-protocols/nats-protocol#hpub
    // HPUB <subject> [reply-to] <#header bytes> <#total bytes>\r\n[headers]\r\n\r\n[payload]\r\n
    public void WritePublish<T>(string subject, string? replyTo, NatsHeaders? headers, T? value, INatsSerialize<T> serializer)
    {
        int ctrlLen;
        if (headers == null)
        {
            // 'PUB '   + subject                                +' '+ payload len        +'\r\n'
            ctrlLen = 4 + _subjectEncoding.GetByteCount(subject) + 1 + MaxIntStringLength + 2;
        }
        else
        {
            // 'HPUB '  + subject                                +' '+ header len         +' '+ payload len        +'\r\n'
            ctrlLen = 5 + _subjectEncoding.GetByteCount(subject) + 1 + MaxIntStringLength + 1 + MaxIntStringLength + 2;
        }

        if (replyTo != null)
        {
            // len  += replyTo                                +' '
            ctrlLen += _subjectEncoding.GetByteCount(replyTo) + 1;
        }

        var span = _writer.GetSpan(ctrlLen);
        if (headers == null)
        {
            span[0] = (byte)'P';
            span[1] = (byte)'U';
            span[2] = (byte)'B';
            span[3] = (byte)' ';
            span = span[4..];
        }
        else
        {
            span[0] = (byte)'H';
            span[1] = (byte)'P';
            span[2] = (byte)'U';
            span[3] = (byte)'B';
            span[4] = (byte)' ';
            span = span[5..];
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

        Span<byte> headersLengthSpan = default;
        if (headers != null)
        {
            headersLengthSpan = span[..MaxIntStringLength];
            span[MaxIntStringLength] = (byte)' ';
            span = span[(MaxIntStringLength + 1)..];
        }

        var totalLengthSpan = span[..MaxIntStringLength];
        span[MaxIntStringLength] = (byte)'\r';
        span[MaxIntStringLength + 1] = (byte)'\n';
        _writer.Advance(ctrlLen);

        var totalLength = 0L;
        if (headers != null)
        {
            var headersLength = _headerWriter.Write(headers);
            headersLengthSpan.OverwriteAllocatedNumber(headersLength);
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

        totalLengthSpan.OverwriteAllocatedNumber(totalLength);
        span = _writer.GetSpan(2);
        span[0] = (byte)'\r';
        span[1] = (byte)'\n';
        _writer.Advance(2);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#sub
    // SUB <subject> [queue group] <sid>
    public void WriteSubscribe(int sid, string subject, string? queueGroup, int? maxMsgs)
    {
        var offset = 0;

        var maxLength = CommandConstants.SubWithPadding.Length
            + subject.Length + 1
            + (queueGroup == null ? 0 : queueGroup.Length + 1)
            + MaxIntStringLength
            + NewLineLength; // newline

        var writableSpan = _writer.GetSpan(maxLength);
        CommandConstants.SubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.SubWithPadding.Length;

        subject.WriteASCIIBytes(writableSpan.Slice(offset));
        offset += subject.Length;
        writableSpan.Slice(offset)[0] = (byte)' ';
        offset += 1;

        if (queueGroup != null)
        {
            queueGroup.WriteASCIIBytes(writableSpan.Slice(offset));
            offset += queueGroup.Length;
            writableSpan.Slice(offset)[0] = (byte)' ';
            offset += 1;
        }

        if (!Utf8Formatter.TryFormat(sid, writableSpan.Slice(offset), out var written))
        {
            throw new NatsException("Can not format integer.");
        }

        offset += written;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        _writer.Advance(offset);

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
        var offset = 0;
        var maxLength = CommandConstants.UnsubWithPadding.Length
            + MaxIntStringLength
            + ((maxMessages != null) ? (1 + MaxIntStringLength) : 0)
            + NewLineLength;

        var writableSpan = _writer.GetSpan(maxLength);
        CommandConstants.UnsubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.UnsubWithPadding.Length;

        if (!Utf8Formatter.TryFormat(sid, writableSpan.Slice(offset), out var written))
        {
            throw new NatsException("Can not format integer.");
        }

        offset += written;

        if (maxMessages != null)
        {
            writableSpan.Slice(offset)[0] = (byte)' ';
            offset += 1;
            if (!Utf8Formatter.TryFormat(maxMessages.Value, writableSpan.Slice(offset), out written))
            {
                throw new NatsException("Can not format integer.");
            }

            offset += written;
        }

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        _writer.Advance(offset);
    }

    internal void WriteRaw(byte[] protocol)
    {
        var span = _writer.GetSpan(protocol.Length);
        protocol.CopyTo(span);
        _writer.Advance(protocol.Length);
    }

    private void WriteConstant(ReadOnlySpan<byte> constant)
    {
        var writableSpan = _writer.GetSpan(constant.Length);
        constant.CopyTo(writableSpan);
        _writer.Advance(constant.Length);
    }
}
