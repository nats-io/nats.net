using System.Buffers;
using System.Text;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core.Internal;

internal class HeaderWriter
{
    private const byte ByteCr = (byte)'\r';
    private const byte ByteLf = (byte)'\n';
    private const byte ByteColon = (byte)':';
    private const byte ByteSpace = (byte)' ';
    private const byte ByteDel = 127;

#if NET8_0_OR_GREATER
    private static readonly SearchValues<byte> ValidKeyBytes = SearchValues.Create(CreateValidKeyBytes());
#endif

    private readonly Encoding _encoding;

    public HeaderWriter(Encoding encoding) => _encoding = encoding;

    private static ReadOnlySpan<byte> CrLf => new[] { ByteCr, ByteLf };

    private static ReadOnlySpan<byte> ColonSpace => new[] { ByteColon, ByteSpace };

    internal static int GetBytesLength(NatsHeaders headers, Encoding encoding)
    {
        var len = CommandConstants.NatsHeaders10NewLine.Length;
        foreach (var kv in headers)
        {
            foreach (var value in kv.Value)
            {
                if (value != null)
                {
                    // key length
                    var keyLength = encoding.GetByteCount(kv.Key);
                    len += keyLength;

                    // colon space length
                    len += ColonSpace.Length;

                    // value length
                    var valueLength = encoding.GetByteCount(value);
                    len += valueLength;

                    // CrLf length
                    len += CrLf.Length;
                }
            }
        }

        // CrLf length for empty headers
        len += CrLf.Length;
        return len;
    }

    // cannot contain ASCII Bytes <=32, 58, or >=127
    internal static bool ValidateKey(ReadOnlySpan<byte> span)
    {
#if NET8_0_OR_GREATER
        return !span.ContainsAnyExcept(ValidKeyBytes);
#else
        foreach (var b in span)
        {
            if (b <= ByteSpace || b == ByteColon || b >= ByteDel)
                return false;
        }

        return true;
#endif
    }

    internal long Write(IBufferWriter<byte> bufferWriter, NatsHeaders headers)
    {
        bufferWriter.WriteSpan(CommandConstants.NatsHeaders10NewLine);
        var len = CommandConstants.NatsHeaders10NewLine.Length;

        foreach (var kv in headers)
        {
            foreach (var value in kv.Value)
            {
                if (value != null)
                {
                    // write key
                    var keyLength = _encoding.GetByteCount(kv.Key);
                    var keySpan = bufferWriter.GetSpan(keyLength);
                    _encoding.GetBytes(kv.Key, keySpan);
                    if (!ValidateKey(keySpan.Slice(0, keyLength)))
                    {
                        NatsException.Throw($"Invalid header key '{kv.Key}': contains colon, space, or other non-printable ASCII characters");
                    }

                    bufferWriter.Advance(keyLength);
                    len += keyLength;

                    bufferWriter.Write(ColonSpace);
                    len += ColonSpace.Length;

                    // write values
                    var valueLength = _encoding.GetByteCount(value);
                    var valueSpan = bufferWriter.GetSpan(valueLength);
                    _encoding.GetBytes(value, valueSpan);
                    if (!ValidateValue(valueSpan.Slice(0, valueLength)))
                    {
                        NatsException.Throw($"Invalid header value for key '{kv.Key}': contains CRLF");
                    }

                    bufferWriter.Advance(valueLength);
                    len += valueLength;

                    bufferWriter.Write(CrLf);
                    len += CrLf.Length;
                }
            }
        }

        // Even empty header needs to terminate.
        // We will send NATS/1.0 version line
        // even if there are no headers.
        bufferWriter.Write(CrLf);
        len += CrLf.Length;

        return len;
    }

#if NET8_0_OR_GREATER
    private static byte[] CreateValidKeyBytes()
    {
        var bytes = new byte[ByteDel - ByteSpace - 2];
        var index = 0;

        for (var b = ByteSpace + 1; b < ByteDel; b++)
        {
            if (b != ByteColon)
            {
                bytes[index++] = (byte)b;
            }
        }

        return bytes;
    }
#endif

    // cannot contain CRLF
    private static bool ValidateValue(ReadOnlySpan<byte> span)
    {
        while (true)
        {
            var pos = span.IndexOf(ByteCr);
            if (pos == -1 || pos == span.Length - 1)
                return true;

            pos += 1;
            if (span[pos] == ByteLf)
                return false;

            span = span[pos..];
        }
    }
}
