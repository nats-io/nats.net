using System.Buffers;

namespace NATS.Client.Core.Internal;

/// <summary>
/// A <see cref="MemoryPool{T}"/> wrapper that clears rented buffers when they
/// are returned, preventing credential data from leaking through pooled memory.
/// </summary>
internal sealed class ClearingMemoryPool : MemoryPool<byte>
{
    private readonly MemoryPool<byte> _inner = MemoryPool<byte>.Shared;

    public override int MaxBufferSize => _inner.MaxBufferSize;

    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        return new ClearingMemoryOwner(_inner.Rent(minBufferSize));
    }

    protected override void Dispose(bool disposing)
    {
    }

    private sealed class ClearingMemoryOwner : IMemoryOwner<byte>
    {
        private IMemoryOwner<byte>? _inner;

        public ClearingMemoryOwner(IMemoryOwner<byte> inner) => _inner = inner;

        public Memory<byte> Memory => _inner?.Memory ?? Memory<byte>.Empty;

        public void Dispose()
        {
            var inner = Interlocked.Exchange(ref _inner, null);
            if (inner == null)
                return;

            inner.Memory.Span.Clear();
            inner.Dispose();
        }
    }
}
