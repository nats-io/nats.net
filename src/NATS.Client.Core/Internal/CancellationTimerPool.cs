using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

// Pool and Node
internal sealed class CancellationTimerPool : IObjectPoolNode<CancellationTimerPool>
{
    private readonly CancellationTokenSource _cancellationTokenSource;

    private CancellationTimerPool? _next;

    public CancellationTimerPool()
    {
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public ref CancellationTimerPool? NextNode => ref _next;

    public CancellationToken Token => _cancellationTokenSource.Token;

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
        if (_cancellationTokenSource.TryReset())
        {
            pool.Return(this);
        }
        else
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }

    public void CancelAfter(TimeSpan delay)
    {
        _cancellationTokenSource.CancelAfter(delay);
    }
}
