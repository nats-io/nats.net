using System.Buffers;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

// similar as ArrayBufferWriter but adds more functional for ProtocolWriter
internal sealed class FixedArrayBufferWriter : IBufferWriter<byte>
{
    private byte[] _buffer;
    private int _written;

    public FixedArrayBufferWriter(int capacity = 65535)
    {
        _buffer = new byte[capacity];
        _written = 0;
    }

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public int WrittenCount => _written;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Range PreAllocate(int size)
    {
        var range = new Range(_written, _written + size);
        Advance(size);
        return range;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpanInPreAllocated(Range range)
    {
        return _buffer.AsSpan(range);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        _written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        _written += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        if (_buffer.Length - _written < sizeHint)
        {
            Resize(sizeHint + _written);
        }

        return _buffer.AsMemory(_written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (_buffer.Length - _written < sizeHint)
        {
            Resize(sizeHint + _written);
        }

        return _buffer.AsSpan(_written);
    }

    private void Resize(int sizeHint)
    {
        Array.Resize(ref _buffer, Math.Max(sizeHint, _buffer.Length * 2));
    }
}
