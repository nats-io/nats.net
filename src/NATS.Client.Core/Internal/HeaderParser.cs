using System.Buffers;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace NATS.Client.Core.Internal;

internal class HeaderParser
{
    private const byte ByteCR = (byte)'\r';
    private const byte ByteLF = (byte)'\n';
    private const byte ByteColon = (byte)':';
    private const byte ByteSpace = (byte)' ';
    private const byte ByteTab = (byte)'\t';

    private readonly Encoding _encoding;

    public HeaderParser(Encoding encoding)
    {
        _encoding = encoding;
    }

    public bool ParseHeaders(in SequenceReader<byte> reader, NatsHeaders headers)
    {
        while (!reader.End)
        {
            var span = reader.UnreadSpan;
            while (span.Length > 0)
            {
                var ch1 = (byte)0;
                var ch2 = (byte)0;
                var readAhead = 0;

                // Fast path, we're still looking at the same span
                if (span.Length >= 2)
                {
                    ch1 = span[0];
                    ch2 = span[1];
                }

                // Possibly split across spans
                else if (reader.TryRead(out ch1))
                {
                    // Note if we read ahead by 1 or 2 bytes
                    readAhead = reader.TryRead(out ch2) ? 2 : 1;
                }

                if (ch1 == ByteCR)
                {
                    // Check for final CRLF.
                    if (ch2 == ByteLF)
                    {
                        // If we got 2 bytes from the span directly so skip ahead 2 so that
                        // the reader's state matches what we expect
                        if (readAhead == 0)
                        {
                            reader.Advance(2);
                        }

                        // Double CRLF found, so end of headers.
                        return true;
                    }
                    else if (readAhead == 1)
                    {
                        // Didn't read 2 bytes, reset the reader so we don't consume anything
                        reader.Rewind(1);
                        return false;
                    }

                    // Headers don't end in CRLF line.
                    Debug.Assert(readAhead == 0 || readAhead == 2, "readAhead == 0 || readAhead == 2");

                    throw new NatsException($"Protocol error: invalid headers, no ending CRLFCRLF");
                }

                var length = 0;

                // We only need to look for the end if we didn't read ahead; otherwise there isn't enough in
                // in the span to contain a header.
                if (readAhead == 0)
                {
                    length = span.IndexOfAny(ByteCR, ByteLF);

                    // If not found length with be -1; casting to uint will turn it to uint.MaxValue
                    // which will be larger than any possible span.Length. This also serves to eliminate
                    // the bounds check for the next lookup of span[length]
                    if ((uint)length < (uint)span.Length)
                    {
                        // Early memory read to hide latency
                        var expectedCR = span[length];

                        // Correctly has a CR, move to next
                        length++;

                        if (expectedCR != ByteCR)
                        {
                            // Sequence needs to be CRLF not LF first.
                            RejectRequestHeader(span[..length]);
                        }

                        if ((uint)length < (uint)span.Length)
                        {
                            // Early memory read to hide latency
                            var expectedLF = span[length];

                            // Correctly has a LF, move to next
                            length++;

                            if (expectedLF != ByteLF ||
                                length < 5 ||

                                // Exclude the CRLF from the headerLine and parse the header name:value pair
                                !TryTakeSingleHeader(span[..(length - 2)], headers))
                            {
                                // Sequence needs to be CRLF and not contain an inner CR not part of terminator.
                                // Less than min possible headerSpan of 5 bytes a:b\r\n
                                // Not parsable as a valid name:value header pair.
                                RejectRequestHeader(span[..length]);
                            }

                            // Read the header successfully, skip the reader forward past the headerSpan.
                            span = span.Slice(length);
                            reader.Advance(length);
                        }
                        else
                        {
                            // No enough data, set length to 0.
                            length = 0;
                        }
                    }
                }

                // End found in current span
                if (length > 0)
                {
                    continue;
                }

                // We moved the reader to look ahead 2 bytes so rewind the reader
                if (readAhead > 0)
                {
                    reader.Rewind(readAhead);
                }

                length = ParseMultiSpanHeader(reader, headers);
                if (length < 0)
                {
                    // Not there
                    return false;
                }

                reader.Advance(length);

                // As we crossed spans set the current span to default
                // so we move to the next span on the next iteration
                span = default;
            }
        }

        return false;
    }

    private int ParseMultiSpanHeader(in SequenceReader<byte> reader, NatsHeaders headers)
    {
        var currentSlice = reader.UnreadSequence;
        var lineEndPosition = currentSlice.PositionOfAny(ByteCR, ByteLF);

        if (lineEndPosition == null)
        {
            // Not there.
            return -1;
        }

        SequencePosition lineEnd;
        ReadOnlySpan<byte> headerSpan;
        if (currentSlice.Slice(reader.Position, lineEndPosition.Value).Length == currentSlice.Length - 1)
        {
            // No enough data, so CRLF can't currently be there.
            // However, we need to check the found char is CR and not LF

            // Advance 1 to include CR/LF in lineEnd
            lineEnd = currentSlice.GetPosition(1, lineEndPosition.Value);
            headerSpan = currentSlice.Slice(reader.Position, lineEnd).ToSpan();
            if (headerSpan[^1] != ByteCR)
            {
                RejectRequestHeader(headerSpan);
            }

            return -1;
        }

        // Advance 2 to include CR{LF?} in lineEnd
        lineEnd = currentSlice.GetPosition(2, lineEndPosition.Value);
        headerSpan = currentSlice.Slice(reader.Position, lineEnd).ToSpan();

        if (headerSpan.Length < 5)
        {
            // Less than min possible headerSpan is 5 bytes a:b\r\n
            RejectRequestHeader(headerSpan);
        }

        if (headerSpan[^2] != ByteCR)
        {
            // Sequence needs to be CRLF not LF first.
            RejectRequestHeader(headerSpan[..^1]);
        }

        if (headerSpan[^1] != ByteLF ||

            // Exclude the CRLF from the headerLine and parse the header name:value pair
            !TryTakeSingleHeader(headerSpan[..^2], headers))
        {
            // Sequence needs to be CRLF and not contain an inner CR not part of terminator.
            // Not parsable as a valid name:value header pair.
            RejectRequestHeader(headerSpan);
        }

        return headerSpan.Length;
    }

    private bool TryTakeSingleHeader(ReadOnlySpan<byte> headerLine, NatsHeaders headers)
    {
        // We are looking for a colon to terminate the header name.
        // However, the header name cannot contain a space or tab so look for all three
        // and see which is found first.
        var nameEnd = headerLine.IndexOfAny(ByteColon, ByteSpace, ByteTab);

        // If not found length with be -1; casting to uint will turn it to uint.MaxValue
        // which will be larger than any possible headerLine.Length. This also serves to eliminate
        // the bounds check for the next lookup of headerLine[nameEnd]
        if ((uint)nameEnd >= (uint)headerLine.Length)
        {
            // Colon not found.
            return false;
        }

        // Early memory read to hide latency
        var expectedColon = headerLine[nameEnd];
        if (nameEnd == 0)
        {
            // Header name is empty.
            return false;
        }

        if (expectedColon != ByteColon)
        {
            // Header name space or tab.
            return false;
        }

        // Skip colon to get to the value start.
        var valueStart = nameEnd + 1;

        // Generally there will only be one space, so we will check it directly
        if ((uint)valueStart < (uint)headerLine.Length)
        {
            var ch = headerLine[valueStart];
            if (ch == ByteSpace || ch == ByteTab)
            {
                // Ignore first whitespace.
                valueStart++;

                // More header chars?
                if ((uint)valueStart < (uint)headerLine.Length)
                {
                    ch = headerLine[valueStart];

                    // Do a fast check; as we now expect non-space, before moving into loop.
                    if (ch <= ByteSpace && (ch == ByteSpace || ch == ByteTab))
                    {
                        valueStart++;

                        // Is more whitespace, so we will loop to find the end. This is the slow path.
                        for (; valueStart < headerLine.Length; valueStart++)
                        {
                            ch = headerLine[valueStart];
                            if (ch != ByteTab && ch != ByteSpace)
                            {
                                // Non-whitespace char found, valueStart is now start of value.
                                break;
                            }
                        }
                    }
                }
            }
        }

        var valueEnd = headerLine.Length - 1;

        // Ignore end whitespace. Generally there will no spaces
        // so we will check the first before moving to a loop.
        if (valueEnd > valueStart)
        {
            var ch = headerLine[valueEnd];

            // Do a fast check; as we now expect non-space, before moving into loop.
            if (ch <= ByteSpace && (ch == ByteSpace || ch == ByteTab))
            {
                // Is whitespace so move to loop
                valueEnd--;
                for (; valueEnd > valueStart; valueEnd--)
                {
                    ch = headerLine[valueEnd];
                    if (ch != ByteTab && ch != ByteSpace)
                    {
                        // Non-whitespace char found, valueEnd is now start of value.
                        break;
                    }
                }
            }
        }

        // Range end is exclusive, so add 1 to valueEnd
        valueEnd++;
        var key = _encoding.GetString(headerLine[..nameEnd]);
        var value = _encoding.GetString(headerLine[valueStart..valueEnd]);
        if (headers.TryGetValue(key, out var existing))
        {
            headers[key] = StringValues.Concat(existing, value);
        }
        else
        {
            headers[key] = value;
        }

        return true;
    }

    [StackTraceHidden]
    private void RejectRequestHeader(ReadOnlySpan<byte> headerLine)
        => throw new NatsException(
            $"Protocol error: invalid request header line '{_encoding.GetString(headerLine)}'");
}
