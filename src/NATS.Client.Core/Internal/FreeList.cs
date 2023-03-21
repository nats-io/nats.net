using System.Diagnostics.CodeAnalysis;

namespace NATS.Client.Core.Internal;

internal sealed class FreeList<T> : IDisposable
    where T : class
{
    private const int InitialCapacity = 4;
    private const int MinShrinkStart = 8;
    private readonly object _gate = new object();

    private T?[] _values;
    private int _count;
    private FastQueue<int> _freeIndex;
    private bool _isDisposed;

    public FreeList()
    {
        Initialize();
    }

    public T?[] GetValues() => _values; // no lock, safe for iterate

    public int GetCount()
    {
        lock (_gate)
        {
            return _count;
        }
    }

    public int Add(T value)
    {
        lock (_gate)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(FreeList<T>));

            if (_freeIndex.Count != 0)
            {
                var index = _freeIndex.Dequeue();
                _values[index] = value;
                _count++;
                return index;
            }
            else
            {
                // resize
                var newValues = new T[_values.Length * 2];
                Array.Copy(_values, 0, newValues, 0, _values.Length);
                _freeIndex.EnsureNewCapacity(newValues.Length);
                for (int i = _values.Length; i < newValues.Length; i++)
                {
                    _freeIndex.Enqueue(i);
                }

                var index = _freeIndex.Dequeue();
                newValues[_values.Length] = value;
                _count++;
                Volatile.Write(ref _values, newValues);
                return index;
            }
        }
    }

    public void Remove(int index, bool shrinkWhenEmpty)
    {
        lock (_gate)
        {
            if (_isDisposed) return; // do nothing

            ref var v = ref _values[index];
            if (v == null) throw new KeyNotFoundException($"key index {index} is not found.");

            v = null;
            _freeIndex.Enqueue(index);
            _count--;

            if (shrinkWhenEmpty && _count == 0 && _values.Length > MinShrinkStart)
            {
                Initialize(); // re-init.
            }
        }
    }

    /// <summary>
    /// Dispose and get cleared count.
    /// </summary>
    public bool TryDispose(out int clearedCount)
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                clearedCount = 0;
                return false;
            }

            clearedCount = _count;
            Dispose();
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _freeIndex = null!;
            _values = Array.Empty<T?>();
            _count = 0;
        }
    }

    [MemberNotNull(nameof(_freeIndex), nameof(_values))]
    private void Initialize()
    {
        _freeIndex = new FastQueue<int>(InitialCapacity);
        for (int i = 0; i < InitialCapacity; i++)
        {
            _freeIndex.Enqueue(i);
        }

        _count = 0;

        var v = new T?[InitialCapacity];
        Volatile.Write(ref _values, v);
    }
}
