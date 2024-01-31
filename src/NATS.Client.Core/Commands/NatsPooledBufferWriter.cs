using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

// adapted from https://github.com/CommunityToolkit/dotnet/blob/v8.2.2/src/CommunityToolkit.HighPerformance/Buffers/ArrayPoolBufferWriter%7BT%7D.cs
internal sealed class NatsPooledBufferWriter<T> : IBufferWriter<T>, IObjectPoolNode<NatsPooledBufferWriter<T>>
{
    private const int DefaultInitialMinBufferSize = 256;
    private const int DefaultInitialMaxBufferSize = 65536;

    private readonly ArrayPool<T> _pool;
    private readonly int _size;
    private T[]? _array;
    private int _index;
    private NatsPooledBufferWriter<T>? _next;

    public NatsPooledBufferWriter(int size)
    {
        if (size < DefaultInitialMinBufferSize)
        {
            size = DefaultInitialMinBufferSize;
        }

        if (size > DefaultInitialMaxBufferSize)
        {
            size = DefaultInitialMaxBufferSize;
        }

        _size = size;
        _pool = ArrayPool<T>.Shared;
        _array = _pool.Rent(size);
        _index = 0;
    }

    public ref NatsPooledBufferWriter<T>? NextNode => ref _next;

    /// <summary>
    /// Gets the data written to the underlying buffer so far, as a <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    public ReadOnlyMemory<T> WrittenMemory
    {
        get
        {
            var array = _array;

            if (array is null)
            {
                ThrowObjectDisposedException();
            }

            return array!.AsMemory(0, _index);
        }
    }

    /// <summary>
    /// Gets the data written to the underlying buffer so far, as a <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    public ReadOnlySpan<T> WrittenSpan
    {
        get
        {
            var array = _array;

            if (array is null)
            {
                ThrowObjectDisposedException();
            }

            return array!.AsSpan(0, _index);
        }
    }

    /// <summary>
    /// Gets the amount of data written to the underlying buffer so far.
    /// </summary>
    public int WrittenCount
    {
        get => _index;
    }

    /// <inheritdoc/>
    public void Advance(int count)
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        if (count < 0)
        {
            ThrowArgumentOutOfRangeExceptionForNegativeCount();
        }

        if (_index > array!.Length - count)
        {
            ThrowArgumentExceptionForAdvancedTooFar();
        }

        _index += count;
    }

    /// <inheritdoc/>
    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckBufferAndEnsureCapacity(sizeHint);

        return _array.AsMemory(_index);
    }

    /// <inheritdoc/>
    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckBufferAndEnsureCapacity(sizeHint);

        return _array.AsSpan(_index);
    }

    public void Reset()
    {
        if (_array != null)
            _pool.Return(_array);
        _array = _pool.Rent(_size);
        _index = 0;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        // See comments in MemoryOwner<T> about this
        if (typeof(T) == typeof(char) &&
            _array is char[] chars)
        {
            return new(chars, 0, _index);
        }

        // Same representation used in Span<T>
        return $"NatsPooledBufferWriter<{typeof(T)}>[{_index}]";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowArgumentOutOfRangeExceptionForNegativeCount() => throw new ArgumentOutOfRangeException("count", "The count can't be a negative value.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowArgumentOutOfRangeExceptionForNegativeSizeHint() => throw new ArgumentOutOfRangeException("sizeHint", "The size hint can't be a negative value.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowArgumentExceptionForAdvancedTooFar() => throw new ArgumentException("The buffer writer has advanced too far.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowObjectDisposedException() => throw new ObjectDisposedException("The current buffer has already been disposed.");

    /// <summary>
    /// Ensures that <see cref="_array"/> has enough free space to contain a given number of new items.
    /// </summary>
    /// <param name="sizeHint">The minimum number of items to ensure space for in <see cref="_array"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckBufferAndEnsureCapacity(int sizeHint)
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        if (sizeHint < 0)
        {
            ThrowArgumentOutOfRangeExceptionForNegativeSizeHint();
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint > array!.Length - _index)
        {
            ResizeBuffer(sizeHint);
        }
    }

    /// <summary>
    /// Resizes <see cref="_array"/> to ensure it can fit the specified number of new items.
    /// </summary>
    /// <param name="sizeHint">The minimum number of items to ensure space for in <see cref="_array"/>.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ResizeBuffer(int sizeHint)
    {
        var minimumSize = (uint)_index + (uint)sizeHint;

        // The ArrayPool<T> class has a maximum threshold of 1024 * 1024 for the maximum length of
        // pooled arrays, and once this is exceeded it will just allocate a new array every time
        // of exactly the requested size. In that case, we manually round up the requested size to
        // the nearest power of two, to ensure that repeated consecutive writes when the array in
        // use is bigger than that threshold don't end up causing a resize every single time.
        if (minimumSize > 1024 * 1024)
        {
            minimumSize = BitOperations.RoundUpToPowerOf2(minimumSize);
        }

        _pool.Resize(ref _array, (int)minimumSize);
    }
}
