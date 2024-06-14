// Adapted from https://github.com/dotnet/aspnetcore/blob/v6.0.18/src/Servers/Kestrel/Core/src/Internal/Http/HttpParser.cs

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Primitives;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

// ReSharper disable ConditionIsAlwaysTrueOrFalse
// ReSharper disable PossiblyImpureMethodCallOnReadonlyVariable
namespace NATS.Client.Core;

public class NatsHeaderParser
{
    private const byte ByteCR = (byte)'\r';
    private const byte ByteLF = (byte)'\n';
    private const byte ByteColon = (byte)':';
    private const byte ByteSpace = (byte)' ';
    private const byte ByteTab = (byte)'\t';
    private static readonly byte[] ByteCRLF = { ByteCR, ByteLF };

    private readonly Encoding _encoding;

    public NatsHeaderParser(Encoding encoding) => _encoding = encoding;

    public bool ParseHeaders(SequenceReader<byte> reader, NatsHeaders headers)
    {
        var isVersionLineRead = false;

        while (!reader.End)
        {
            if (reader.TryReadTo(out ReadOnlySpan<byte> line, ByteCRLF))
            {
                if (!isVersionLineRead)
                {
                    if (!TryParseHeaderLine(line, headers))
                    {
                        RejectRequestHeader(line);
                    }

                    isVersionLineRead = true;
                    continue;
                }

                if (line.Length > 0)
                {
                    if (!TryTakeSingleHeader(line, headers))
                    {
                        RejectRequestHeader(line);
                    }
                }
            }
            else
            {
                RejectRequestHeader(reader.Sequence.ToSpan());
            }
        }

        return isVersionLineRead;
    }

    private bool TryParseHeaderLine(ReadOnlySpan<byte> headerLine, NatsHeaders headers)
    {
        // We are first looking for a version line
        // e.g. NATS/1.0 100 Idle Heartbeat
        headerLine.Split(out var versionBytes, out headerLine);

        if (!versionBytes.SequenceEqual(CommandConstants.NatsHeaders10))
        {
            throw new NatsException("Protocol error: header version mismatch");
        }

        if (headerLine.Length != 0)
        {
            headerLine.Split(out var codeBytes, out headerLine);
            if (!Utf8Parser.TryParse(codeBytes, out int code, out _))
                throw new NatsException("Protocol error: header code is not a number");
            headers.Code = code;
        }

        if (headerLine.Length != 0)
        {
            // We can reduce string allocations by detecting commonly used
            // header messages.
            if (headerLine.SequenceEqual(NatsHeaders.MessageIdleHeartbeat))
            {
                headers.Message = NatsHeaders.Messages.IdleHeartbeat;
                headers.MessageText = NatsHeaders.MessageIdleHeartbeatStr;
            }
            else if (headerLine.SequenceEqual(NatsHeaders.MessageBadRequest))
            {
                headers.Message = NatsHeaders.Messages.BadRequest;
                headers.MessageText = NatsHeaders.MessageBadRequestStr;
            }
            else if (headerLine.SequenceEqual(NatsHeaders.MessageConsumerDeleted))
            {
                headers.Message = NatsHeaders.Messages.ConsumerDeleted;
                headers.MessageText = NatsHeaders.MessageConsumerDeletedStr;
            }
            else if (headerLine.SequenceEqual(NatsHeaders.MessageConsumerIsPushBased))
            {
                headers.Message = NatsHeaders.Messages.ConsumerIsPushBased;
                headers.MessageText = NatsHeaders.MessageConsumerIsPushBasedStr;
            }
            else if (headerLine.SequenceEqual(NatsHeaders.MessageNoMessages))
            {
                headers.Message = NatsHeaders.Messages.NoMessages;
                headers.MessageText = NatsHeaders.MessageNoMessagesStr;
            }
            else if (headerLine.SequenceEqual(NatsHeaders.MessageRequestTimeout))
            {
                headers.Message = NatsHeaders.Messages.RequestTimeout;
                headers.MessageText = NatsHeaders.MessageRequestTimeoutStr;
            }
            else if (headerLine.SequenceEqual(NatsHeaders.MessageMessageSizeExceedsMaxBytes))
            {
                headers.Message = NatsHeaders.Messages.MessageSizeExceedsMaxBytes;
                headers.MessageText = NatsHeaders.MessageMessageSizeExceedsMaxBytesStr;
            }
            else
            {
                headers.Message = NatsHeaders.Messages.Text;
                headers.MessageText = _encoding.GetString(headerLine);
            }
        }

        return true;
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
            $"Protocol error: invalid request header line '{headerLine.Dump()}'");
}
