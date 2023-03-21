using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NATS.Client.Core.Internal;

internal sealed class SeqeunceBuilder
{
    private SequenceSegment? _first;
    private SequenceSegment? _last;
    private int _length;

    public SeqeunceBuilder()
    {
    }

    public int Count => _length;

    public ReadOnlySequence<byte> ToReadOnlySequence() => new ReadOnlySequence<byte>(_first!, 0, _last!, _last!.Memory.Length);

    // Memory is only allowed rent from ArrayPool.
    public void Append(ReadOnlyMemory<byte> buffer)
    {
        if (_length == 0 && _first == null)
        {
            var first = SequenceSegment.Create(buffer);
            _first = first;
            _last = first;
            _length = buffer.Length;
        }
        else
        {
            // if append same and continuous buffer, edit memory.
            if (MemoryMarshal.TryGetArray(_last!.Memory, out var array1) && MemoryMarshal.TryGetArray(buffer, out var array2))
            {
                if (array1.Array == array2.Array)
                {
                    if (array1.Offset + array1.Count == array2.Offset)
                    {
                        var newMemory = array1.Array.AsMemory(array1.Offset, array1.Count + array2.Count);
                        _last.SetMemory(newMemory);
                        _length += buffer.Length;
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
            _last!.SetNextSegment(newLast);
            newLast.SetRunningIndex(_length);
            _last = newLast;
            _length += buffer.Length;
        }
    }

    public void AdvanceTo(SequencePosition start)
    {
        Debug.Assert(_first != null, "First segment");
        Debug.Assert(_last != null, "Last segment");

        var segment = (SequenceSegment)start.GetObject()!;
        var index = start.GetInteger();

        // try to find matched segment
        var target = _first;
        while (target != null && target != segment)
        {
            var t = target;
            target = (SequenceSegment)target.Next!;
            t.Return(); // return to pool.
        }

        if (target == null)
            throw new InvalidOperationException("failed to find next segment.");

        _length -= (int)target.RunningIndex + index;
        target.SetMemory(target.Memory.Slice(index));
        target.SetRunningIndex(0);
        _first = target;

        // change all after node runningIndex
        var runningIndex = _first.Memory.Length;
        target = (SequenceSegment?)_first.Next;
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
    private static readonly ConcurrentQueue<SequenceSegment> Pool = new();

    private SequenceSegment()
    {
    }

    public static SequenceSegment Create(ReadOnlyMemory<byte> buffer)
    {
        if (!Pool.TryDequeue(out var result))
        {
            result = new SequenceSegment();
        }

        result.SetMemory(buffer);

        return result;
    }

    internal void SetMemory(ReadOnlyMemory<byte> buffer)
    {
        Memory = buffer;
    }

    internal void SetRunningIndex(long runningIndex)
    {
        // RunningIndex: The sum of node lengths before the current node.
        RunningIndex = runningIndex;
    }

    internal void SetNextSegment(SequenceSegment? nextSegment)
    {
        Next = nextSegment;
    }

    internal void Return()
    {
        // guranteed does not add another segment in same memory so can return buffer in this.
        if (MemoryMarshal.TryGetArray(Memory, out var array) && array.Array != null)
        {
            ArrayPool<byte>.Shared.Return(array.Array);
        }

        Memory = default;
        RunningIndex = 0;
        Next = null;
        Pool.Enqueue(this);
    }
}
