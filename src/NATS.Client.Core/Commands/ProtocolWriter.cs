using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class ProtocolWriter
{
    private const int MaxIntStringLength = 10; // int.MaxValue.ToString().Length
    private const int NewLineLength = 2; // \r\n

    private readonly FixedArrayBufferWriter _writer; // where T : IBufferWriter<byte>

    public ProtocolWriter(FixedArrayBufferWriter writer)
    {
        _writer = writer;
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#connect
    // CONNECT {["option_name":option_value],...}
    public void WriteConnect(ClientOptions options)
    {
        WriteConstant(CommandConstants.ConnectWithPadding);

        var jsonWriter = new Utf8JsonWriter(_writer);
        JsonSerializer.Serialize(jsonWriter, options, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

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
    // PUB <subject> [reply-to] <#bytes>\r\n[payload]
    // To omit the payload, set the payload size to 0, but the second CRLF is still required.
    public void WritePublish(string subject, string? replyTo, ReadOnlySequence<byte> payload)
    {
        var offset = 0;
        var maxLength = CommandConstants.PubWithPadding.Length
            + subject.Length + 1 // with space padding
            + (replyTo == null ? 0 : replyTo.Length + 1)
            + MaxIntStringLength
            + NewLineLength
            + (int)payload.Length
            + NewLineLength;

        var writableSpan = _writer.GetSpan(maxLength);

        CommandConstants.PubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.PubWithPadding.Length;

        subject.WriteASCIIBytes(writableSpan.Slice(offset));
        offset += subject.Length;
        writableSpan.Slice(offset)[0] = (byte)' ';
        offset += 1;

        if (replyTo != null)
        {
            replyTo.WriteASCIIBytes(writableSpan.Slice(offset));
            offset += replyTo.Length;
            writableSpan.Slice(offset)[0] = (byte)' ';
            offset += 1;
        }

        if (!Utf8Formatter.TryFormat(payload.Length, writableSpan.Slice(offset), out var written))
        {
            throw new NatsException("Can not format integer.");
        }

        offset += written;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        if (payload.Length != 0)
        {
            payload.CopyTo(writableSpan.Slice(offset));
            offset += (int)payload.Length;
        }

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        _writer.Advance(offset);
    }

    public void WritePublish<T>(string subject, string? replyTo, T? value, INatsSerializer serializer)
    {
        var offset = 0;
        var maxLengthWithoutPayload = CommandConstants.PubWithPadding.Length
            + subject.Length + 1
            + (replyTo == null ? 0 : replyTo.Length + 1)
            + MaxIntStringLength
            + NewLineLength;

        var writableSpan = _writer.GetSpan(maxLengthWithoutPayload);

        CommandConstants.PubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.PubWithPadding.Length;

        subject.WriteASCIIBytes(writableSpan.Slice(offset));
        offset += subject.Length;
        writableSpan.Slice(offset)[0] = (byte)' ';
        offset += 1;

        if (replyTo != null)
        {
            replyTo.WriteASCIIBytes(writableSpan.Slice(offset));
            offset += replyTo.Length;
            writableSpan.Slice(offset)[0] = (byte)' ';
            offset += 1;
        }

        // Advance for written.
        _writer.Advance(offset);

        // preallocate range for write #bytes(write after serialized)
        var preallocatedRange = _writer.PreAllocate(MaxIntStringLength);
        offset += MaxIntStringLength;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        _writer.Advance(CommandConstants.NewLine.Length);

        var payloadLength = serializer.Serialize(_writer, value);
        var payloadLengthSpan = _writer.GetSpanInPreAllocated(preallocatedRange);
        payloadLengthSpan.Fill((byte)' ');
        if (!Utf8Formatter.TryFormat(payloadLength, payloadLengthSpan, out var written))
        {
            throw new NatsException("Can not format integer.");
        }

        WriteConstant(CommandConstants.NewLine);
    }

    public void WritePublish<T>(string subject, ReadOnlyMemory<byte> inboxPrefix, int id, T? value, INatsSerializer serializer)
    {
        Span<byte> idBytes = stackalloc byte[10];
        if (Utf8Formatter.TryFormat(id, idBytes, out var written))
        {
            idBytes = idBytes.Slice(0, written);
        }

        var offset = 0;
        var maxLengthWithoutPayload = CommandConstants.PubWithPadding.Length
            + subject.Length + 1
            + (inboxPrefix.Length + idBytes.Length + 1) // with space
            + MaxIntStringLength
            + NewLineLength;

        var writableSpan = _writer.GetSpan(maxLengthWithoutPayload);

        CommandConstants.PubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.PubWithPadding.Length;

        subject.WriteASCIIBytes(writableSpan.Slice(offset));
        offset += subject.Length;
        writableSpan.Slice(offset)[0] = (byte)' ';
        offset += 1;

        // build reply-to
        inboxPrefix.Span.CopyTo(writableSpan.Slice(offset));
        offset += inboxPrefix.Length;
        idBytes.CopyTo(writableSpan.Slice(offset));
        offset += idBytes.Length;
        writableSpan.Slice(offset)[0] = (byte)' ';
        offset += 1;

        // Advance for written.
        _writer.Advance(offset);

        // preallocate range for write #bytes(write after serialized)
        var preallocatedRange = _writer.PreAllocate(MaxIntStringLength);
        offset += MaxIntStringLength;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        _writer.Advance(CommandConstants.NewLine.Length);

        var payloadLength = serializer.Serialize(_writer, value);
        var payloadLengthSpan = _writer.GetSpanInPreAllocated(preallocatedRange);
        payloadLengthSpan.Fill((byte)' ');
        if (!Utf8Formatter.TryFormat(payloadLength, payloadLengthSpan, out written))
        {
            throw new NatsException("Can not format integer.");
        }

        WriteConstant(CommandConstants.NewLine);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#sub
    // SUB <subject> [queue group] <sid>
    public void WriteSubscribe(int sid, string subject, string? queueGroup)
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
