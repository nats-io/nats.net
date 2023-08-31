using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class ProtocolWriter
{
    private const int MaxIntStringLength = 10; // int.MaxValue.ToString().Length
    private const int NewLineLength = 2; // \r\n

    private readonly FixedArrayBufferWriter _writer; // where T : IBufferWriter<byte>
    private readonly FixedArrayBufferWriter _bufferHeaders = new();
    private readonly FixedArrayBufferWriter _bufferPayload = new();
    private readonly HeaderWriter _headerWriter = new(Encoding.UTF8);

    public ProtocolWriter(FixedArrayBufferWriter writer)
    {
        _writer = writer;
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
        // We use a separate buffer to write the headers so that we can calculate the
        // size before we write to the output buffer '_writer'.
        if (headers != null)
        {
            _bufferHeaders.Reset();
            _headerWriter.Write(_bufferHeaders, headers);
        }

        // Start writing the message to buffer:
        // PUP / HPUB
        _writer.WriteSpan(headers == null ? CommandConstants.PubWithPadding : CommandConstants.HPubWithPadding);
        _writer.WriteASCIIAndSpace(subject);

        if (replyTo != null)
        {
            _writer.WriteASCIIAndSpace(replyTo);
        }

        if (headers == null)
        {
            _writer.WriteNumber(payload.Length);
        }
        else
        {
            var headersLength = _bufferHeaders.WrittenSpan.Length;
            _writer.WriteNumber(CommandConstants.NatsHeaders10NewLine.Length + headersLength);
            _writer.WriteSpace();
            var total = CommandConstants.NatsHeaders10NewLine.Length + headersLength + payload.Length;
            _writer.WriteNumber(total);
        }

        // End of message first line
        _writer.WriteNewLine();

        if (headers != null)
        {
            _writer.WriteSpan(CommandConstants.NatsHeaders10NewLine);
            _writer.WriteSpan(_bufferHeaders.WrittenSpan);
        }

        if (payload.Length != 0)
        {
            _writer.WriteSequence(payload);
        }

        _writer.WriteNewLine();
    }

    public void WritePublish<T>(string subject, string? replyTo, NatsHeaders? headers, T? value, INatsSerializer serializer)
    {
        _bufferPayload.Reset();

        // Consider null as empty payload. This way we are able to transmit null values as sentinels.
        // Another point is serializer behaviour. For instance JSON serializer seems to serialize null
        // as a string "null", others might throw exception.
        if (value != null)
        {
            serializer.Serialize(_bufferPayload, value);
        }

        var payload = new ReadOnlySequence<byte>(_bufferPayload.WrittenMemory);
        WritePublish(subject, replyTo, headers, payload);
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
