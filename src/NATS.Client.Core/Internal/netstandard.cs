// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable ConvertToPrimaryConstructor
// ReSharper disable RedundantCast
// ReSharper disable SuggestVarOrType_Elsewhere

#pragma warning disable SA1403
#pragma warning disable SA1204
#pragma warning disable SA1405

#if NETSTANDARD2_0 || NETSTANDARD2_1

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}

namespace NATS.Client.Core.Internal.NetStandardExtensions
{
    using System.Buffers;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;

    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal readonly struct VoidResult
    {
    }

    internal sealed class TaskCompletionSource : TaskCompletionSource<VoidResult>
    {
        public TaskCompletionSource(TaskCreationOptions creationOptions)
            : base(creationOptions)
        {
        }

        public new Task Task => base.Task;

        public bool TrySetResult() => TrySetResult(default);

        public void SetResult() => SetResult(default);
    }

    internal static class TaskExtensionsCommon
    {
        internal static Task WaitAsync(this Task task, CancellationToken cancellationToken)
            => WaitAsync(task, Timeout.InfiniteTimeSpan, cancellationToken);

        internal static Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            var timeoutTask = Task.Delay(timeout, cancellationToken);

#pragma warning disable VSTHRD105
            return Task.WhenAny(task, timeoutTask).ContinueWith(
#pragma warning restore VSTHRD105
                // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
                completedTask =>
                {
#pragma warning disable VSTHRD103
                    if (completedTask.Result == timeoutTask)
#pragma warning restore VSTHRD103
                    {
                        throw new TimeoutException("The operation has timed out.");
                    }

                    return task;
                },
                cancellationToken).Unwrap();
        }
    }

    internal static class SequenceReaderExtensions
    {
        public static bool TryReadTo(this ref SequenceReader<byte> reader, out ReadOnlySpan<byte> result, ReadOnlySpan<byte> delimiters)
        {
            if (reader.TryReadTo(out var buffer, delimiters))
            {
                result = buffer.ToSpan();
                return true;
            }

            result = default;
            return false;
        }
    }

    internal class Random
    {
        [ThreadStatic]
        private static System.Random? _internal;

        internal static Random Shared { get; } = new();

        private static System.Random LocalRandom => _internal ?? Create();

        internal double NextDouble() => LocalRandom.NextDouble();

        internal int Next(int minValue, int maxValue) => LocalRandom.Next(minValue, maxValue);

        internal long NextInt64(long minValue, long maxValue)
        {
            if (minValue > maxValue)
                throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must be less than or equal to maxValue");

            var range = (double)maxValue - (double)minValue + 1;
            var sample = NextDouble();
            var scaled = sample * range;

            return (long)(scaled + minValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static System.Random Create() => _internal = new System.Random();
    }

    internal static class SpanExtensionsCommon
    {
        internal static bool Contains(this Span<byte> span, byte value) => span.IndexOf(value) >= 0;
    }

    internal class PeriodicTimer : IDisposable
    {
        private readonly Timer _timer;
        private readonly TimeSpan _period;
        private TaskCompletionSource<bool> _tcs;
        private bool _disposed;

        public PeriodicTimer(TimeSpan period)
        {
            _period = period;
            _timer = new Timer(Callback, null, period, Timeout.InfiniteTimeSpan);
            _tcs = new TaskCompletionSource<bool>();
        }

        public Task<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PeriodicTimer));

            _timer.Change(_period, Timeout.InfiniteTimeSpan);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<bool>(cancellationToken);

            cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));

            return _tcs.Task;
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            _tcs.TrySetResult(false); // Signal no more ticks will occur
        }

        private void Callback(object state)
        {
            var tcs = Interlocked.Exchange(ref _tcs, new TaskCompletionSource<bool>());
            tcs.TrySetResult(true);
        }
    }

    internal static class ReadOnlySequenceExtensions
    {
        // Adapted from .NET 6.0 implementation
        internal static long GetOffset<T>(this in ReadOnlySequence<T> sequence, SequencePosition position)
        {
            var positionSequenceObject = position.GetObject();
            var positionIsNull = positionSequenceObject == null;

            var startObject = sequence.Start.GetObject();
            var endObject = sequence.End.GetObject();

            var positionIndex = (uint)position.GetInteger();

            // if a sequence object is null, we suppose start segment
            if (positionIsNull)
            {
                positionSequenceObject = sequence.Start.GetObject();
                positionIndex = (uint)sequence.Start.GetInteger();
            }

            // Single-Segment Sequence
            if (startObject == endObject)
            {
                return positionIndex;
            }
            else
            {
                // Verify position validity, this is not covered by BoundsCheck for Multi-Segment Sequence
                // BoundsCheck for Multi-Segment Sequence check only validity inside a current sequence but not for SequencePosition validity.
                // For single segment position bound check it is implicit.
                Debug.Assert(positionSequenceObject != null);

                if (((ReadOnlySequenceSegment<T>)positionSequenceObject!).Memory.Length - positionIndex < 0)
                    throw new ArgumentOutOfRangeException();

                // Multi-Segment Sequence
                var currentSegment = (ReadOnlySequenceSegment<T>?)startObject;
                while (currentSegment != null && currentSegment != positionSequenceObject)
                {
                    currentSegment = currentSegment.Next!;
                }

                // Hit the end of the segments but didn't find the segment
                if (currentSegment is null)
                {
                    throw new ArgumentOutOfRangeException();
                }

                Debug.Assert(currentSegment!.RunningIndex + positionIndex >= 0);

                return currentSegment!.RunningIndex + positionIndex;
            }
        }
    }

    internal static class EncodingExtensionsCommon
    {
        internal static string GetString(this Encoding encoding, in ReadOnlySequence<byte> buffer)
            => encoding.GetString(buffer.ToArray());

        internal static void GetBytes(this Encoding encoding, string chars, IBufferWriter<byte> bw)
        {
            var buffer = encoding.GetBytes(chars);
            bw.Write(buffer);
        }
    }
}

#endif

#if NETSTANDARD2_0

namespace NATS.Client.Core.Internal.NetStandardExtensions
{
    using System.Text;

    internal static class EncodingExtensions
    {
        internal static int GetBytes(this Encoding encoding, string chars, Span<byte> bytes)
        {
            var buffer = encoding.GetBytes(chars);
            buffer.AsSpan().CopyTo(bytes);
            return buffer.Length;
        }

        internal static string GetString(this Encoding encoding, in ReadOnlySpan<byte> buffer)
        {
            return encoding.GetString(buffer.ToArray());
        }
    }

    internal static class TaskExtensions
    {
        internal static bool IsNotCompletedSuccessfully(this Task? task)
        {
            return task != null && (!task.IsCompleted || task.IsCanceled || task.IsFaulted);
        }
    }

    internal static class KeyValuePairExtensions
    {
        internal static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kv, out TKey key, out TValue value)
        {
            key = kv.Key;
            value = kv.Value;
        }
    }
}

#endif
