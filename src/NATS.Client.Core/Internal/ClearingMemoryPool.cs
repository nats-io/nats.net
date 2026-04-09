using System.Buffers;

namespace NATS.Client.Core.Internal;

/// <summary>
/// A <see cref="MemoryPool{T}"/> wrapper that clears rented buffers when they
/// are returned, preventing credential data from leaking through pooled memory.
/// </summary>
internal sealed class ClearingMemoryPool : MemoryPool<byte>
{
    private readonly ObjectPool<ClearingMemoryOwner> _wrappers = new(256);

    public override int MaxBufferSize => int.MaxValue;

    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        if (!_wrappers.TryPop(out var wrapper))
        {
            wrapper = new ClearingMemoryOwner(this);
        }

        wrapper.SetArray(ArrayPool<byte>.Shared.Rent(minBufferSize == -1 ? 4096 : minBufferSize));
        return wrapper;
    }

    protected override void Dispose(bool disposing)
    {
    }

    private sealed class ClearingMemoryOwner : IMemoryOwner<byte>, IObjectPoolNode<ClearingMemoryOwner>
    {
        private readonly ClearingMemoryPool _pool;
        private ClearingMemoryOwner? _nextNode;
        private byte[]? _array;

        public ClearingMemoryOwner(ClearingMemoryPool pool) => _pool = pool;

        public ref ClearingMemoryOwner? NextNode => ref _nextNode;

        public Memory<byte> Memory => _array;

        public void SetArray(byte[] array) => _array = array;

        public void Dispose()
        {
            var array = Interlocked.Exchange(ref _array, null);
            if (array == null)
                return;

            ArrayPool<byte>.Shared.Return(array, clearArray: true);
            _pool._wrappers.TryPush(this);
        }
    }
}
