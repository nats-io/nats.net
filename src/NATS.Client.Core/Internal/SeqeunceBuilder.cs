using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NATS.Client.Core.Internal;

internal sealed class SeqeunceBuilder
{
    SequenceSegment? first;
    SequenceSegment? last;
    int length;

    public ReadOnlySequence<byte> ToReadOnlySequence() => new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);

    public int Count => length;

    public SeqeunceBuilder()
    {
    }

    // Memory is only allowed rent from ArrayPool.
    public void Append(ReadOnlyMemory<byte> buffer)
    {
        if (length == 0 && first == null)
        {
            var first = SequenceSegment.Create(buffer);
            this.first = first;
            last = first;
            length = buffer.Length;
        }
        else
        {
            // if append same and continuous buffer, edit memory.
            if (MemoryMarshal.TryGetArray(last!.Memory, out var array1) && MemoryMarshal.TryGetArray(buffer, out var array2))
            {
                if (array1.Array == array2.Array)
                {
                    if (array1.Offset + array1.Count == array2.Offset)
                    {
                        var newMemory = array1.Array.AsMemory(array1.Offset, array1.Count + array2.Count);
                        last.SetMemory(newMemory);
                        length += buffer.Length;
                        return;
                    }
                    else
                    {
                        // not allowd for this operation for return buffer.
                        throw new InvalidOperationException("Append buffer is not continous buffer.");
                    }
                }
            }

            // others, append new segment to last.
            var newLast = SequenceSegment.Create(buffer);
            last!.SetNextSegment(newLast);
            newLast.SetRunningIndex(length);
            last = newLast;
            length += buffer.Length;
        }
    }

    public void AdvanceTo(SequencePosition start)
    {
        Debug.Assert(first != null);
        Debug.Assert(last != null);

        var segment = (SequenceSegment)start.GetObject()!;
        var index = start.GetInteger();

        // try to find matched segment
        var target = first;
        while (target != null && target != segment)
        {
            var t = target;
            target = (SequenceSegment)target.Next!;
            t.Return(); // return to pool.
        }

        if (target == null) throw new InvalidOperationException("failed to find next segment.");

        length -= ((int)target.RunningIndex + index);
        target.SetMemory(target.Memory.Slice(index));
        target.SetRunningIndex(0);
        first = target;

        // change all after node runningIndex
        var runningIndex = first.Memory.Length;
        target = (SequenceSegment?)first.Next;
        while (target != null)
        {
            target.SetRunningIndex(runningIndex);
            runningIndex += target.Memory.Length;
            target = (SequenceSegment?)target.Next;
        }
    }
}

internal class SequenceSegment : ReadOnlySequenceSegment<byte>
{
    static readonly ConcurrentQueue<SequenceSegment> pool = new();

    public static SequenceSegment Create(ReadOnlyMemory<byte> buffer)
    {
        if (!pool.TryDequeue(out var result))
        {
            result = new SequenceSegment();
        }

        result.SetMemory(buffer);

        return result;
    }

    SequenceSegment()
    {
    }

    internal void SetMemory(ReadOnlyMemory<byte> buffer)
    {
        Memory = buffer;
    }

    internal void SetRunningIndex(long runningIndex)
    {
        // RunningIndex: The sum of node lengths before the current node.
        this.RunningIndex = runningIndex;
    }

    internal void SetNextSegment(SequenceSegment? nextSegment)
    {
        this.Next = nextSegment;
    }

    internal void Return()
    {
        // guranteed does not add another segment in same memory so can return buffer in this.
        if (MemoryMarshal.TryGetArray(this.Memory, out var array) && array.Array != null)
        {
            ArrayPool<byte>.Shared.Return(array.Array);
        }

        this.Memory = default;
        this.RunningIndex = 0;
        this.Next = null;
        pool.Enqueue(this);
    }
}
