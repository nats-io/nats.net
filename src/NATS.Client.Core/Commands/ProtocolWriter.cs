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
    private readonly MemoryBufferWriter<byte> _ctrlBuf = new(new byte[4096]); // https://github.com/nats-io/nats-server/blob/26f0a9bd0f0574073977db069ff4cea2ecbbcac4/server/const.go#L65

    public ProtocolWriter(PipeWriter writer, Encoding headerEncoding)
    {
        _writer = writer;
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
    public void WritePublish(string subject, string? replyTo, NatsHeaders? headers, ReadOnlySequence<byte> payload)
    {
        _ctrlBuf.Clear();

        // Start writing the message to buffer:
        // PUP / HPUB
        _ctrlBuf.WriteSpan(headers == null ? CommandConstants.PubWithPadding : CommandConstants.HPubWithPadding);
        _ctrlBuf.WriteASCIIAndSpace(subject);

        if (replyTo != null)
        {
            _ctrlBuf.WriteASCIIAndSpace(replyTo);
        }

        if (headers == null)
        {
            _ctrlBuf.WriteNumber(payload.Length);
            _ctrlBuf.WriteNewLine();
            _writer.WriteSpan(_ctrlBuf.WrittenSpan);
        }
        else
        {
            var headersLengthPos = _ctrlBuf.WrittenCount;
            _ctrlBuf.Advance(MaxIntStringLength);
            _ctrlBuf.WriteSpace();

            var totalLengthPos = _ctrlBuf.WrittenCount;
            _ctrlBuf.Advance(MaxIntStringLength);
            _ctrlBuf.WriteNewLine();
            _ctrlBuf.WriteSpan(CommandConstants.NatsHeaders10NewLine);

            var writerSpan = _writer.GetSpan(_ctrlBuf.WrittenSpan.Length);
            _ctrlBuf.WrittenSpan.CopyTo(writerSpan);
            _writer.Advance(_ctrlBuf.WrittenSpan.Length);

            var headersLengthSpan = writerSpan.Slice(headersLengthPos, MaxIntStringLength);
            var totalLengthSpan = writerSpan.Slice(totalLengthPos, MaxIntStringLength);

            var headersLength = _headerWriter.Write(headers);
            headersLengthSpan.OverwriteAllocatedNumber(CommandConstants.NatsHeaders10NewLine.Length + headersLength);
            totalLengthSpan.OverwriteAllocatedNumber(CommandConstants.NatsHeaders10NewLine.Length + headersLength + payload.Length);
        }

        if (payload.Length != 0)
        {
            _writer.WriteSequence(payload);
        }

        _writer.WriteNewLine();
    }

    public void WritePublish<T>(string subject, string? replyTo, NatsHeaders? headers, T? value, INatsSerialize<T> serializer)
    {
        _ctrlBuf.Clear();

        // Start writing the message to buffer:
        // PUP / HPUB
        _ctrlBuf.WriteSpan(headers == null ? CommandConstants.PubWithPadding : CommandConstants.HPubWithPadding);
        _ctrlBuf.WriteASCIIAndSpace(subject);

        if (replyTo != null)
        {
            _ctrlBuf.WriteASCIIAndSpace(replyTo);
        }

        long totalLength = 0;
        Span<byte> totalLengthSpan;
        if (headers == null)
        {
            var totalLengthPos = _ctrlBuf.WrittenCount;
            _ctrlBuf.Advance(MaxIntStringLength);
            _ctrlBuf.WriteNewLine();

            var writerSpan = _writer.GetSpan(_ctrlBuf.WrittenSpan.Length);
            _ctrlBuf.WrittenSpan.CopyTo(writerSpan);
            _writer.Advance(_ctrlBuf.WrittenSpan.Length);

            totalLengthSpan = writerSpan.Slice(totalLengthPos, MaxIntStringLength);
        }
        else
        {
            var headersLengthPos = _ctrlBuf.WrittenCount;
            _ctrlBuf.Advance(MaxIntStringLength);
            _ctrlBuf.WriteSpace();

            var totalLengthPos = _ctrlBuf.WrittenCount;
            _ctrlBuf.Advance(MaxIntStringLength);
            _ctrlBuf.WriteNewLine();
            _ctrlBuf.WriteSpan(CommandConstants.NatsHeaders10NewLine);

            var writerSpan = _writer.GetSpan(_ctrlBuf.WrittenSpan.Length);
            _ctrlBuf.WrittenSpan.CopyTo(writerSpan);
            _writer.Advance(_ctrlBuf.WrittenSpan.Length);

            var headersLengthSpan = writerSpan.Slice(headersLengthPos, MaxIntStringLength);
            totalLengthSpan = writerSpan.Slice(totalLengthPos, MaxIntStringLength);

            var headersLength = _headerWriter.Write(headers);
            headersLengthSpan.OverwriteAllocatedNumber(CommandConstants.NatsHeaders10NewLine.Length + headersLength);
            totalLength += CommandConstants.NatsHeaders10NewLine.Length + headersLength;
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
        _writer.WriteNewLine();
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
