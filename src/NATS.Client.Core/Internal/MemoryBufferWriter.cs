// adapted from https://github.com/CommunityToolkit/dotnet/blob/main/src/CommunityToolkit.HighPerformance/Buffers/MemoryBufferWriter%7BT%7D.cs

using System.Buffers;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

/// <summary>
/// Represents an output sink into which <typeparamref name="T"/> data can be written, backed by a <see cref="Memory{T}"/> instance.
/// </summary>
/// <typeparam name="T">The type of items to write to the current instance.</typeparam>
/// <remarks>
/// This is a custom <see cref="IBufferWriter{T}"/> implementation that wraps a <see cref="Memory{T}"/> instance.
/// It can be used to bridge APIs consuming an <see cref="IBufferWriter{T}"/> with existing <see cref="Memory{T}"/>
/// instances (or objects that can be converted to a <see cref="Memory{T}"/>), to ensure the data is written directly
/// to the intended buffer, with no possibility of doing additional allocations or expanding the available capacity.
/// </remarks>
public sealed class MemoryBufferWriter<T> : IBufferWriter<T>
{
    /// <summary>
    /// The underlying <see cref="Memory{T}"/> instance.
    /// </summary>
    private readonly Memory<T> _memory;

#pragma warning disable IDE0032 // Use field over auto-property (like in ArrayPoolBufferWriter<T>)
    /// <summary>
    /// The starting offset within <see cref="_memory"/>.
    /// </summary>
    private int _index;
#pragma warning restore IDE0032

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryBufferWriter{T}"/> class.
    /// </summary>
    /// <param name="memory">The target <see cref="Memory{T}"/> instance to write to.</param>
    public MemoryBufferWriter(Memory<T> memory) => _memory = memory;

    public ReadOnlyMemory<T> WrittenMemory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _memory.Slice(0, _index);
    }

    public ReadOnlySpan<T> WrittenSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _memory.Slice(0, _index).Span;
    }

    public int WrittenCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _index;
    }

    public int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _memory.Length;
    }

    public int FreeCapacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _memory.Length - _index;
    }

    public void Clear()
    {
        _memory.Slice(0, _index).Span.Clear();
        _index = 0;
    }

    public void Advance(int count)
    {
        if (count < 0)
        {
            ThrowArgumentOutOfRangeExceptionForNegativeCount();
        }

        if (_index > _memory.Length - count)
        {
            ThrowArgumentExceptionForAdvancedTooFar();
        }

        _index += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        ValidateSizeHint(sizeHint);

        return _memory.Slice(_index);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        ValidateSizeHint(sizeHint);

        return _memory.Slice(_index).Span;
    }

    /// <summary>
    /// Validates the requested size for either <see cref="GetMemory"/> or <see cref="GetSpan"/>.
    /// </summary>
    /// <param name="sizeHint">The minimum number of items to ensure space for in <see cref="_memory"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateSizeHint(int sizeHint)
    {
        if (sizeHint < 0)
        {
            ThrowArgumentOutOfRangeExceptionForNegativeSizeHint();
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint > FreeCapacity)
        {
            ThrowArgumentExceptionForCapacityExceeded();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        // See comments in MemoryOwner<T> about this
        if (typeof(T) == typeof(char))
        {
            return _memory.Slice(0, _index).ToString();
        }

        // Same representation used in Span<T>
        return $"CommunityToolkit.HighPerformance.Buffers.MemoryBufferWriter<{typeof(T)}>[{_index}]";
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the requested count is negative.
    /// </summary>
    private static void ThrowArgumentOutOfRangeExceptionForNegativeCount()
    {
        throw new ArgumentOutOfRangeException("count", "The count can't be a negative value.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the size hint is negative.
    /// </summary>
    private static void ThrowArgumentOutOfRangeExceptionForNegativeSizeHint()
    {
        throw new ArgumentOutOfRangeException("sizeHint", "The size hint can't be a negative value.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the requested count is negative.
    /// </summary>
    private static void ThrowArgumentExceptionForAdvancedTooFar()
    {
        throw new ArgumentException("The buffer writer has advanced too far.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> when the requested size exceeds the capacity.
    /// </summary>
    private static void ThrowArgumentExceptionForCapacityExceeded()
    {
        throw new ArgumentException("The buffer writer doesn't have enough capacity left.");
    }
}
