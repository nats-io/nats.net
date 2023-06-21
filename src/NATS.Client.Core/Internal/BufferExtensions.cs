// Adapted from https://github.com/dotnet/aspnetcore/blob/v6.0.18/src/Shared/ServerInfrastructure/BufferExtensions.cs

#nullable enable

using System.Buffers;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

internal static class BufferExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> ToSpan(this ReadOnlySequence<byte> buffer)
    {
        if (buffer.IsSingleSegment)
        {
            return buffer.FirstSpan;
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Returns position of first occurrence of item in the <see cref="ReadOnlySequence{T}"/>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SequencePosition? PositionOfAny<T>(in this ReadOnlySequence<T> source, T value0, T value1)
        where T : IEquatable<T>
    {
        if (source.IsSingleSegment)
        {
            int index = source.First.Span.IndexOfAny(value0, value1);
            if (index != -1)
            {
                return source.GetPosition(index);
            }

            return null;
        }
        else
        {
            return PositionOfAnyMultiSegment(source, value0, value1);
        }
    }

    private static SequencePosition? PositionOfAnyMultiSegment<T>(in ReadOnlySequence<T> source, T value0, T value1)
        where T : IEquatable<T>
    {
        SequencePosition position = source.Start;
        SequencePosition result = position;
        while (source.TryGet(ref position, out ReadOnlyMemory<T> memory))
        {
            int index = memory.Span.IndexOfAny(value0, value1);
            if (index != -1)
            {
                return source.GetPosition(index, result);
            }
            else if (position.GetObject() == null)
            {
                break;
            }

            result = position;
        }

        return null;
    }
}
