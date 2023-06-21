using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core.Internal;

internal static class BufferWriterExtensions
{
    private const int MaxIntStringLength = 10; // int.MaxValue.ToString().Length

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteNewLine(this FixedArrayBufferWriter writer)
    {
        var span = writer.GetSpan(CommandConstants.NewLine.Length);
        CommandConstants.NewLine.CopyTo(span);
        writer.Advance(CommandConstants.NewLine.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteNumber(this FixedArrayBufferWriter writer, long number)
    {
        var span = writer.GetSpan(MaxIntStringLength);
        if (!Utf8Formatter.TryFormat(number, span, out var writtenLength))
        {
            throw new NatsException("Can not format integer.");
        }

        writer.Advance(writtenLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteSpace(this FixedArrayBufferWriter writer)
    {
        var span = writer.GetSpan(1);
        span[0] = (byte)' ';
        writer.Advance(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteSpan(this FixedArrayBufferWriter writer, ReadOnlySpan<byte> span)
    {
        var writerSpan = writer.GetSpan(span.Length);
        span.CopyTo(writerSpan);
        writer.Advance(span.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteSequence(this FixedArrayBufferWriter writer, ReadOnlySequence<byte> sequence)
    {
        var len = (int)sequence.Length;
        var span = writer.GetSpan(len);
        sequence.CopyTo(span);
        writer.Advance(len);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteASCIIAndSpace(this FixedArrayBufferWriter writer, string ascii)
    {
        var span = writer.GetSpan(ascii.Length + 1);
        ascii.WriteASCIIBytes(span);
        span[ascii.Length] = (byte)' ';
        writer.Advance(ascii.Length + 1);
    }
}
