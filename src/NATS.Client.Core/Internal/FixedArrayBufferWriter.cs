using System.Buffers;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

// similar as ArrayBufferWriter but adds more functional for ProtocolWriter
internal sealed class FixedArrayBufferWriter : ICountableBufferWriter
{
    byte[] buffer;
    int written;

    public ReadOnlyMemory<byte> WrittenMemory => buffer.AsMemory(0, written);
    public int WrittenCount => written;

    public FixedArrayBufferWriter(int capacity = 65535)
    {
        buffer = new byte[capacity];
        written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Range PreAllocate(int size)
    {
        var range = new Range(written, written + size);
        Advance(size);
        return range;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpanInPreAllocated(Range range)
    {
        return buffer.AsSpan(range);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        written += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        if (buffer.Length - written < sizeHint)
        {
            Resize(sizeHint);
        }
        return buffer.AsMemory(written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (buffer.Length - written < sizeHint)
        {
            Resize(sizeHint);
        }
        return buffer.AsSpan(written);
    }

    void Resize(int sizeHint)
    {
        Array.Resize(ref buffer, Math.Max(sizeHint, buffer.Length * 2));
    }
}
