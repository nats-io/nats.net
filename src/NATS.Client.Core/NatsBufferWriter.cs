// adapted from https://github.com/CommunityToolkit/dotnet/blob/main/src/CommunityToolkit.HighPerformance/Buffers/NatsBufferWriter%7BT%7D.cs

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BitOperations = System.Numerics.BitOperations;

namespace NATS.Client.Core;

/// <summary>
/// Represents a heap-based, array-backed output sink into which <typeparamref name="T"/> data can be written.
/// </summary>
/// <typeparam name="T">The type of items to write to the current instance.</typeparam>
public sealed class NatsBufferWriter<T> : IBufferWriter<T>, IMemoryOwner<T>
{
    /// <summary>
    /// The default buffer size to use to expand empty arrays.
    /// </summary>
    private const int DefaultInitialBufferSize = 256;

    /// <summary>
    /// The <see cref="ArrayPool{T}"/> instance used to rent <see cref="_array"/>.
    /// </summary>
    private readonly ArrayPool<T> _pool;

    /// <summary>
    /// The underlying <typeparamref name="T"/> array.
    /// </summary>
    private T[]? _array;

#pragma warning disable IDE0032 // Use field over auto-property (clearer and faster)
    /// <summary>
    /// The starting offset within <see cref="_array"/>.
    /// </summary>
    private int _index;
#pragma warning restore IDE0032

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsBufferWriter{T}"/> class.
    /// </summary>
    public NatsBufferWriter()
        : this(ArrayPool<T>.Shared, DefaultInitialBufferSize)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsBufferWriter{T}"/> class.
    /// </summary>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> instance to use.</param>
    public NatsBufferWriter(ArrayPool<T> pool)
        : this(pool, DefaultInitialBufferSize)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsBufferWriter{T}"/> class.
    /// </summary>
    /// <param name="initialCapacity">The minimum capacity with which to initialize the underlying buffer.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="initialCapacity"/> is not valid.</exception>
    public NatsBufferWriter(int initialCapacity)
        : this(ArrayPool<T>.Shared, initialCapacity)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsBufferWriter{T}"/> class.
    /// </summary>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> instance to use.</param>
    /// <param name="initialCapacity">The minimum capacity with which to initialize the underlying buffer.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="initialCapacity"/> is not valid.</exception>
    public NatsBufferWriter(ArrayPool<T> pool, int initialCapacity)
    {
        // Since we're using pooled arrays, we can rent the buffer with the
        // default size immediately, we don't need to use lazy initialization
        // to save unnecessary memory allocations in this case.
        // Additionally, we don't need to manually throw the exception if
        // the requested size is not valid, as that'll be thrown automatically
        // by the array pool in use when we try to rent an array with that size.
        _pool = pool;
        _array = pool.Rent(initialCapacity);
        _index = 0;
    }

    /// <inheritdoc/>
    Memory<T> IMemoryOwner<T>.Memory
    {
        // This property is explicitly implemented so that it's hidden
        // under normal usage, as the name could be confusing when
        // displayed besides WrittenMemory and GetMemory().
        // The IMemoryOwner<T> interface is implemented primarily
        // so that the AsStream() extension can be used on this type,
        // allowing users to first create a NatsBufferWriter<byte>
        // instance to write data to, then get a stream through the
        // extension and let it take care of returning the underlying
        // buffer to the shared pool when it's no longer necessary.
        // Inlining is not needed here since this will always be a callvirt.
        get => MemoryMarshal.AsMemory(WrittenMemory);
    }

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

    /// <summary>
    /// Gets the total amount of space within the underlying buffer.
    /// </summary>
    public int Capacity
    {
        get
        {
            var array = _array;

            if (array is null)
            {
                ThrowObjectDisposedException();
            }

            return array!.Length;
        }
    }

    /// <summary>
    /// Gets the amount of space available that can still be written into without forcing the underlying buffer to grow.
    /// </summary>
    public int FreeCapacity
    {
        get
        {
            var array = _array;

            if (array is null)
            {
                ThrowObjectDisposedException();
            }

            return array!.Length - _index;
        }
    }

    /// <summary>
    /// Clears the data written to the underlying buffer.
    /// </summary>
    /// <remarks>
    /// You must clear the buffer instance before trying to re-use it.
    /// </remarks>
    public void Clear()
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        array.AsSpan(0, _index).Clear();

        _index = 0;
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

    /// <summary>
    /// Gets an <see cref="ArraySegment{T}"/> instance wrapping the underlying <typeparamref name="T"/> array in use.
    /// </summary>
    /// <returns>An <see cref="ArraySegment{T}"/> instance wrapping the underlying <typeparamref name="T"/> array in use.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the buffer in use has already been disposed.</exception>
    /// <remarks>
    /// This method is meant to be used when working with APIs that only accept an array as input, and should be used with caution.
    /// In particular, the returned array is rented from an array pool, and it is responsibility of the caller to ensure that it's
    /// not used after the current <see cref="NatsBufferWriter{T}"/> instance is disposed. Doing so is considered undefined
    /// behavior, as the same array might be in use within another <see cref="NatsBufferWriter{T}"/> instance.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArraySegment<T> DangerousGetArray()
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        return new(array!, 0, _index);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var array = _array;

        if (array is null)
        {
            return;
        }

        _array = null;

        _pool.Return(array);
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
        return $"CommunityToolkit.HighPerformance.Buffers.NatsBufferWriter<{typeof(T)}>[{_index}]";
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
    /// Throws an <see cref="ObjectDisposedException"/> when <see cref="_array"/> is <see langword="null"/>.
    /// </summary>
    private static void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException("The current buffer has already been disposed.");
    }

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

internal static class NatsBufferWriterExtensions
{
    /// <summary>
    /// Changes the number of elements of a rented one-dimensional array to the specified new size.
    /// </summary>
    /// <typeparam name="T">The type of items into the target array to resize.</typeparam>
    /// <param name="pool">The target <see cref="ArrayPool{T}"/> instance to use to resize the array.</param>
    /// <param name="array">The rented <typeparamref name="T"/> array to resize, or <see langword="null"/> to create a new array.</param>
    /// <param name="newSize">The size of the new array.</param>
    /// <param name="clearArray">Indicates whether the contents of the array should be cleared before reuse.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="newSize"/> is less than 0.</exception>
    /// <remarks>When this method returns, the caller must not use any references to the old array anymore.</remarks>
    public static void Resize<T>(this ArrayPool<T> pool, [NotNull] ref T[]? array, int newSize, bool clearArray = false)
    {
        // If the old array is null, just create a new one with the requested size
        if (array is null)
        {
            array = pool.Rent(newSize);

            return;
        }

        // If the new size is the same as the current size, do nothing
        if (array.Length == newSize)
        {
            return;
        }

        // Rent a new array with the specified size, and copy as many items from the current array
        // as possible to the new array. This mirrors the behavior of the Array.Resize API from
        // the BCL: if the new size is greater than the length of the current array, copy all the
        // items from the original array into the new one. Otherwise, copy as many items as possible,
        // until the new array is completely filled, and ignore the remaining items in the first array.
        var newArray = pool.Rent(newSize);
        var itemsToCopy = Math.Min(array.Length, newSize);

        Array.Copy(array, 0, newArray, 0, itemsToCopy);

        pool.Return(array, clearArray);

        array = newArray;
    }
}
