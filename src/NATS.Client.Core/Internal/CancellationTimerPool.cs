using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

// Pool and Node
internal sealed class CancellationTimerPool : IObjectPoolNode<CancellationTimerPool>
{
    CancellationTimerPool? next;
    public ref CancellationTimerPool? NextNode => ref next;

    readonly CancellationTokenSource cancellationTokenSource;

    public CancellationToken Token => cancellationTokenSource.Token;

    public CancellationTimerPool()
    {
        this.cancellationTokenSource = new CancellationTokenSource();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CancellationTimerPool Rent(ObjectPool pool)
    {
        if (pool.TryRent<CancellationTimerPool>(out var self))
        {
            return self;
        }
        else
        {
            return new CancellationTimerPool();
        }
    }

    public void Return(ObjectPool pool)
    {
        if (cancellationTokenSource.TryReset())
        {
            pool.Return(this);
        }
        else
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
    }

    public void CancelAfter(TimeSpan delay)
    {
        cancellationTokenSource.CancelAfter(delay);
    }
}
