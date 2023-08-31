using System.Buffers;
using System.Text;

namespace NATS.Client.Core.Internal;

public static class DebuggingExtensions
{
    public static string Dump(this ReadOnlySequence<byte> buffer)
    {
        var sb = new StringBuilder();
        foreach (var readOnlyMemory in buffer)
        {
            sb.Append(Dump(readOnlyMemory.Span));
        }

        return sb.ToString();
    }

    public static string Dump(this ReadOnlySpan<byte> span)
    {
        var sb = new StringBuilder();
        foreach (char b in span)
        {
            switch (b)
            {
            case >= ' ' and <= '~':
                sb.Append(b);
                break;
            case '\r':
                sb.Append("\\r");
                break;
            case '\n':
                sb.Append("\\n");
                break;
            default:
                sb.Append('.');
                break;
            }
        }

        return sb.ToString();
    }

    public static string Dump(this NatsHeaders? headers)
    {
        if (headers == null)
            return "<NULL>";

        var sb = new StringBuilder();
        sb.AppendLine($"{headers.Version} {headers.Code} {headers.Message} {headers.MessageText}");
        foreach (var (key, stringValues) in headers)
        {
            foreach (var value in stringValues)
            {
                sb.AppendLine($"{key}: {value}");
            }
        }

        return sb.ToString();
    }
}
