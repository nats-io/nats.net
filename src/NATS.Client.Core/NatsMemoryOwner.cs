// adapted from https://github.com/CommunityToolkit/dotnet/blob/v8.2.1/src/CommunityToolkit.HighPerformance/Buffers/MemoryOwner%7BT%7D.cs
// changed from class to struct for non-nullable deserialization

namespace NATS.Client.Core;

#pragma warning disable SX1101

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// An <see langword="enum"/> that indicates a mode to use when allocating buffers.
/// </summary>
public enum NatsMemoryOwnerAllocationMode
{
    /// <summary>
    /// The default allocation mode for pooled memory (rented buffers are not cleared).
    /// </summary>
    Default,

    /// <summary>
    /// Clear pooled buffers when renting them.
    /// </summary>
    Clear,
}

/// <summary>
/// An <see cref="IMemoryOwner{T}"/> implementation with an embedded length and a fast <see cref="Span{T}"/> accessor.
/// </summary>
/// <typeparam name="T">The type of items to store in the current instance.</typeparam>
public struct NatsMemoryOwner<T> : IMemoryOwner<T>
{
    /// <summary>
    /// The starting offset within <see cref="_array"/>.
    /// </summary>
    private readonly int _start;

#pragma warning disable IDE0032
    /// <summary>
    /// The usable length within <see cref="_array"/> (starting from <see cref="_start"/>).
    /// </summary>
    private readonly int _length;
#pragma warning restore IDE0032

    /// <summary>
    /// The <see cref="ArrayPool{T}"/> instance used to rent <see cref="_array"/>.
    /// </summary>
    private readonly ArrayPool<T> _pool;

    /// <summary>
    /// The underlying <typeparamref name="T"/> array.
    /// </summary>
    private T[]? _array;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsMemoryOwner{T}"/> class with the specified parameters.
    /// </summary>
    /// <param name="length">The length of the new memory buffer to use.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> instance to use.</param>
    /// <param name="mode">Indicates the allocation mode to use for the new buffer to rent.</param>
    private NatsMemoryOwner(int length, ArrayPool<T> pool, NatsMemoryOwnerAllocationMode mode)
    {
        _start = 0;
        _length = length;
        _pool = pool;
        _array = pool.Rent(length);

        if (mode == NatsMemoryOwnerAllocationMode.Clear)
        {
            _array.AsSpan(0, length).Clear();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsMemoryOwner{T}"/> class with the specified parameters.
    /// </summary>
    /// <param name="start">The starting offset within <paramref name="array"/>.</param>
    /// <param name="length">The length of the array to use.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> instance currently in use.</param>
    /// <param name="array">The input <typeparamref name="T"/> array to use.</param>
    private NatsMemoryOwner(int start, int length, ArrayPool<T> pool, T[] array)
    {
        _start = start;
        _length = length;
        _pool = pool;
        _array = array;
    }

    /// <summary>
    /// Gets an empty <see cref="NatsMemoryOwner{T}"/> instance.
    /// </summary>
    public static NatsMemoryOwner<T> Empty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0, ArrayPool<T>.Shared, NatsMemoryOwnerAllocationMode.Default);
    }

    /// <summary>
    /// Gets the number of items in the current instance
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    /// <summary>
    /// Creates a new <see cref="NatsMemoryOwner{T}"/> instance with the specified parameters.
    /// </summary>
    /// <param name="size">The length of the new memory buffer to use.</param>
    /// <returns>A <see cref="NatsMemoryOwner{T}"/> instance of the requested length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size"/> is not valid.</exception>
    /// <remarks>This method is just a proxy for the <see langword="private"/> constructor, for clarity.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static NatsMemoryOwner<T> Allocate(int size) => new(size, ArrayPool<T>.Shared, NatsMemoryOwnerAllocationMode.Default);

    /// <summary>
    /// Creates a new <see cref="NatsMemoryOwner{T}"/> instance with the specified parameters.
    /// </summary>
    /// <param name="size">The length of the new memory buffer to use.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> instance currently in use.</param>
    /// <returns>A <see cref="NatsMemoryOwner{T}"/> instance of the requested length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size"/> is not valid.</exception>
    /// <remarks>This method is just a proxy for the <see langword="private"/> constructor, for clarity.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static NatsMemoryOwner<T> Allocate(int size, ArrayPool<T> pool) => new(size, pool, NatsMemoryOwnerAllocationMode.Default);

    /// <summary>
    /// Creates a new <see cref="NatsMemoryOwner{T}"/> instance with the specified parameters.
    /// </summary>
    /// <param name="size">The length of the new memory buffer to use.</param>
    /// <param name="mode">Indicates the allocation mode to use for the new buffer to rent.</param>
    /// <returns>A <see cref="NatsMemoryOwner{T}"/> instance of the requested length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size"/> is not valid.</exception>
    /// <remarks>This method is just a proxy for the <see langword="private"/> constructor, for clarity.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static NatsMemoryOwner<T> Allocate(int size, NatsMemoryOwnerAllocationMode mode) => new(size, ArrayPool<T>.Shared, mode);

    /// <summary>
    /// Creates a new <see cref="NatsMemoryOwner{T}"/> instance with the specified parameters.
    /// </summary>
    /// <param name="size">The length of the new memory buffer to use.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> instance currently in use.</param>
    /// <param name="mode">Indicates the allocation mode to use for the new buffer to rent.</param>
    /// <returns>A <see cref="NatsMemoryOwner{T}"/> instance of the requested length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size"/> is not valid.</exception>
    /// <remarks>This method is just a proxy for the <see langword="private"/> constructor, for clarity.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static NatsMemoryOwner<T> Allocate(int size, ArrayPool<T> pool, NatsMemoryOwnerAllocationMode mode) => new(size, pool, mode);

    /// <inheritdoc/>
#pragma warning disable SA1201
    public Memory<T> Memory
#pragma warning restore SA1201
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var array = _array;

            if (array is null)
            {
                return Memory<T>.Empty;
            }

            return new(array!, _start, _length);
        }
    }

    /// <summary>
    /// Gets a <see cref="Span{T}"/> wrapping the memory belonging to the current instance.
    /// </summary>
    public Span<T> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var array = _array;

            if (array is null)
            {
                return Span<T>.Empty;
            }

            ref var r0 = ref array!.DangerousGetReferenceAt(_start);

            // On .NET 6+ runtimes, we can manually create a span from the starting reference to
            // skip the argument validations, which include an explicit null check, covariance check
            // for the array and the actual validation for the starting offset and target length. We
            // only do this on .NET 6+ as we can leverage the runtime-specific array layout to get
            // a fast access to the initial element, which makes this trick worth it. Otherwise, on
            // runtimes where we would need to at least access a static field to retrieve the base
            // byte offset within an SZ array object, we can get better performance by just using the
            // default Span<T> constructor and paying the cost of the extra conditional branches,
            // especially if T is a value type, in which case the covariance check is JIT removed.
            return MemoryMarshal.CreateSpan(ref r0, _length);
        }
    }

    /// <summary>
    /// Returns a reference to the first element within the current instance, with no bounds check.
    /// </summary>
    /// <returns>A reference to the first element within the current instance.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the buffer in use has already been disposed.</exception>
    /// <remarks>
    /// This method does not perform bounds checks on the underlying buffer, but does check whether
    /// the buffer itself has been disposed or not. This check should not be removed, and it's also
    /// the reason why the method to get a reference at a specified offset is not present.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T DangerousGetReference()
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        return ref array!.DangerousGetReferenceAt(_start);
    }

    /// <summary>
    /// Gets an <see cref="ArraySegment{T}"/> instance wrapping the underlying <typeparamref name="T"/> array in use.
    /// </summary>
    /// <returns>An <see cref="ArraySegment{T}"/> instance wrapping the underlying <typeparamref name="T"/> array in use.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the buffer in use has already been disposed.</exception>
    /// <remarks>
    /// This method is meant to be used when working with APIs that only accept an array as input, and should be used with caution.
    /// In particular, the returned array is rented from an array pool, and it is responsibility of the caller to ensure that it's
    /// not used after the current <see cref="NatsMemoryOwner{T}"/> instance is disposed. Doing so is considered undefined behavior,
    /// as the same array might be in use within another <see cref="NatsMemoryOwner{T}"/> instance.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArraySegment<T> DangerousGetArray()
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        return new(array!, _start, _length);
    }

    /// <summary>
    /// Slices the buffer currently in use and returns a new <see cref="NatsMemoryOwner{T}"/> instance.
    /// </summary>
    /// <param name="start">The starting offset within the current buffer.</param>
    /// <param name="length">The length of the buffer to use.</param>
    /// <returns>A new <see cref="NatsMemoryOwner{T}"/> instance using the target range of items.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the buffer in use has already been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="start"/> or <paramref name="length"/> are not valid.</exception>
    /// <remarks>
    /// Using this method will dispose the current instance, and should only be used when an oversized
    /// buffer is rented and then adjusted in size, to avoid having to rent a new buffer of the new
    /// size and copy the previous items into the new one, or needing an additional variable/field
    /// to manually handle to track the used range within a given <see cref="NatsMemoryOwner{T}"/> instance.
    /// </remarks>
    public NatsMemoryOwner<T> Slice(int start, int length)
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        _array = null;

        if ((uint)start > _length)
        {
            ThrowInvalidOffsetException();
        }

        if ((uint)length > (_length - start))
        {
            ThrowInvalidLengthException();
        }

        // We're transferring the ownership of the underlying array, so the current
        // instance no longer needs to be disposed. Because of this, we can manually
        // suppress the finalizer to reduce the overhead on the garbage collector.
        GC.SuppressFinalize(this);

        return new(start, length, _pool, array!);
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
        // Normally we would throw if the array has been disposed,
        // but in this case we'll just return the non formatted
        // representation as a fallback, since the ToString method
        // is generally expected not to throw exceptions.
        if (typeof(T) == typeof(char) &&
            _array is char[] chars)
        {
            return new(chars, _start, _length);
        }

        // Same representation used in Span<T>
        return $"CommunityToolkit.HighPerformance.Buffers.MemoryOwner<{typeof(T)}>[{_length}]";
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> when <see cref="_array"/> is <see langword="null"/>.
    /// </summary>
    private static void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException(nameof(NatsMemoryOwner<T>), "The current buffer has already been disposed");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the <see cref="_start"/> is invalid.
    /// </summary>
    private static void ThrowInvalidOffsetException()
    {
        throw new ArgumentOutOfRangeException(nameof(_start), "The input start parameter was not valid");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the <see cref="_length"/> is invalid.
    /// </summary>
    private static void ThrowInvalidLengthException()
    {
        throw new ArgumentOutOfRangeException(nameof(_length), "The input length parameter was not valid");
    }
}

internal static class NatsMemoryOwnerArrayExtensions
{
    /// <summary>
    /// Returns a reference to the first element within a given <typeparamref name="T"/> array, with no bounds checks.
    /// </summary>
    /// <typeparam name="T">The type of elements in the input <typeparamref name="T"/> array instance.</typeparam>
    /// <param name="array">The input <typeparamref name="T"/> array instance.</param>
    /// <returns>A reference to the first element within <paramref name="array"/>, or the location it would have used, if <paramref name="array"/> is empty.</returns>
    /// <remarks>This method doesn't do any bounds checks, therefore it is responsibility of the caller to perform checks in case the returned value is dereferenced.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T DangerousGetReference<T>(this T[] array)
    {
        return ref MemoryMarshal.GetArrayDataReference(array);
    }

    /// <summary>
    /// Returns a reference to an element at a specified index within a given <typeparamref name="T"/> array, with no bounds checks.
    /// </summary>
    /// <typeparam name="T">The type of elements in the input <typeparamref name="T"/> array instance.</typeparam>
    /// <param name="array">The input <typeparamref name="T"/> array instance.</param>
    /// <param name="i">The index of the element to retrieve within <paramref name="array"/>.</param>
    /// <returns>A reference to the element within <paramref name="array"/> at the index specified by <paramref name="i"/>.</returns>
    /// <remarks>This method doesn't do any bounds checks, therefore it is responsibility of the caller to ensure the <paramref name="i"/> parameter is valid.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T DangerousGetReferenceAt<T>(this T[] array, int i)
    {
        ref var r0 = ref MemoryMarshal.GetArrayDataReference(array);
        ref var ri = ref Unsafe.Add(ref r0, (nint)(uint)i);
#pragma warning disable CS8619
        return ref ri;
#pragma warning restore CS8619
    }
}
