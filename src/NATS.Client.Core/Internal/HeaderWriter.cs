using System.Buffers;
using System.IO.Pipelines;
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

    private readonly Encoding _encoding;

    public HeaderWriter(Encoding encoding) => _encoding = encoding;

    private static ReadOnlySpan<byte> CrLf => new[] { ByteCr, ByteLf };

    private static ReadOnlySpan<byte> ColonSpace => new[] { ByteColon, ByteSpace };

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
                        throw new NatsException(
                            $"Invalid header key '{kv.Key}': contains colon, space, or other non-printable ASCII characters");
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
                        throw new NatsException($"Invalid header value for key '{kv.Key}': contains CRLF");
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

    // cannot contain ASCII Bytes <=32, 58, or 127
    private static bool ValidateKey(ReadOnlySpan<byte> span)
    {
        foreach (var b in span)
        {
            if (b <= ByteSpace || b == ByteColon || b >= ByteDel)
            {
                return false;
            }
        }

        return true;
    }

    // cannot contain CRLF
    private static bool ValidateValue(ReadOnlySpan<byte> span)
    {
        while (true)
        {
            var pos = span.IndexOf(ByteCr);
            if (pos == -1 || pos == span.Length - 1)
            {
                return true;
            }

            pos += 1;
            if (span[pos] == ByteLf)
            {
                return false;
            }

            span = span[pos..];
        }
    }
}
