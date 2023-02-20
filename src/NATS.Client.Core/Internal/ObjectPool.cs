using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

internal sealed class ObjectPool
{
    static int typeId = -1; // Increment by IdentityGenerator<T>

    readonly object gate = new object();
    readonly int poolLimit;
    object[] poolNodes = new object[4]; // ObjectPool<T>[]

    // pool-limit per type.
    public ObjectPool(int poolLimit)
    {
        this.poolLimit = poolLimit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRent<T>([NotNullWhen(true)] out T? value)
        where T : class, IObjectPoolNode<T>
    {
        // poolNodes is grow only, safe to access indexer with no-lock
        var id = IdentityGenerator<T>.Identity;
        if (id < poolNodes.Length && poolNodes[id] is ObjectPool<T> pool)
        {
            return pool.TryPop(out value);
        }
        Grow<T>(id);
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Return<T>(T value)
        where T : class, IObjectPoolNode<T>
    {
        var id = IdentityGenerator<T>.Identity;
        if (id < poolNodes.Length && poolNodes[id] is ObjectPool<T> pool)
        {
            return pool.TryPush(value);
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Grow<T>(int id)
        where T : class, IObjectPoolNode<T>
    {
        lock (gate)
        {
            if (poolNodes.Length <= id)
            {
                Array.Resize(ref poolNodes, Math.Max(poolNodes.Length * 2, id + 1));
                poolNodes[id] = new ObjectPool<T>(poolLimit);
            }
            else if (poolNodes[id] == null)
            {
                poolNodes[id] = new ObjectPool<T>(poolLimit);
            }
            else
            {
                // other thread already created new ObjectPool<T> so do nothing.
            }
        }
    }

    // avoid for Dictionary<Type, ***> lookup cost.
    static class IdentityGenerator<T>
    {
        public static int Identity;

        static IdentityGenerator()
        {
            Identity = Interlocked.Increment(ref typeId);
        }
    }
}

internal interface IObjectPoolNode<T>
{
    ref T? NextNode { get; }
}

internal sealed class ObjectPool<T>
    where T : class, IObjectPoolNode<T>
{
    int gate;
    int size;
    T? root;
    readonly int limit;

    public ObjectPool(int limit)
    {
        this.limit = limit;
    }

    public int Size => size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPop([NotNullWhen(true)] out T? result)
    {
        // Instead of lock, use CompareExchange gate.
        // In a worst case, missed cached object(create new one) but it's not a big deal.
        if (Interlocked.CompareExchange(ref gate, 1, 0) == 0)
        {
            var v = root;
            if (!(v is null))
            {
                ref var nextNode = ref v.NextNode;
                root = nextNode;
                nextNode = null;
                size--;
                result = v;
                Volatile.Write(ref gate, 0);
                return true;
            }

            Volatile.Write(ref gate, 0);
        }
        result = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPush(T item)
    {
        if (Interlocked.CompareExchange(ref gate, 1, 0) == 0)
        {
            if (size < limit)
            {
                item.NextNode = root;
                root = item;
                size++;
                Volatile.Write(ref gate, 0);
                return true;
            }
            else
            {
                Volatile.Write(ref gate, 0);
            }
        }
        return false;
    }
}
