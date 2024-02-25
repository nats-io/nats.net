namespace NATS.Client.Core.Internal;

// Support efficiently cancellation support for connection-dispose/timeout/cancel-per-operation
internal sealed class CancellationTimerPool
{
    private readonly ObjectPool _pool;
    private readonly CancellationToken _rootToken;

    public CancellationTimerPool(ObjectPool pool, CancellationToken rootToken)
    {
        _pool = pool;
        _rootToken = rootToken;
    }

    public CancellationTimer Start(TimeSpan timeout, CancellationToken externalCancellationToken)
    {
        return CancellationTimer.Start(_pool, _rootToken, timeout, externalCancellationToken);
    }
}

internal sealed class CancellationTimer : IObjectPoolNode<CancellationTimer>
{
    // underyling source
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ObjectPool _pool;
    private readonly CancellationToken _rootToken;

    // timer itself is ObjectPool Node
    private CancellationTimer? _next;
    private bool _calledExternalTokenCancel;
    private TimeSpan _timeout;
    private CancellationToken _externalCancellationToken;
    private CancellationTokenRegistration _externalTokenRegistration;

    // this timer pool is tightly coupled with rootToken lifetime(e.g. connection lifetime).
    private CancellationTimer(ObjectPool pool, CancellationToken rootToken)
    {
        _pool = pool;
        _rootToken = rootToken;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(rootToken);
    }

    public ref CancellationTimer? NextNode => ref _next;

    public CancellationToken Token => _cancellationTokenSource.Token;

    public static CancellationTimer Start(ObjectPool pool, CancellationToken rootToken, TimeSpan timeout, CancellationToken externalCancellationToken)
    {
        if (!pool.TryRent<CancellationTimer>(out var self))
        {
            self = new CancellationTimer(pool, rootToken);
        }

        // Timeout with external cancellationToken
        self._externalCancellationToken = externalCancellationToken;
        if (externalCancellationToken.CanBeCanceled)
        {
            self._externalTokenRegistration = externalCancellationToken.UnsafeRegister(
                static state =>
                {
                    var self = (CancellationTimer)state!;
                    self._calledExternalTokenCancel = true;
                    self._cancellationTokenSource.Cancel();
                },
                self);
        }

        self._timeout = timeout;
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            self._cancellationTokenSource.CancelAfter(timeout);
        }

        return self;
    }

    public Exception GetExceptionWhenCanceled()
    {
        if (_rootToken.IsCancellationRequested)
        {
            return new NatsException("Operation is canceled because connection is disposed.");
        }

        if (_externalCancellationToken.IsCancellationRequested)
        {
            return new OperationCanceledException(_externalCancellationToken);
        }

        return new TimeoutException($"Nats operation is canceled due to the configured timeout of {_timeout.TotalSeconds} seconds elapsing.");
    }

    // We can check cancel is called(calling) by return value
    public bool TryReturn()
    {
        if (_externalTokenRegistration.Token.CanBeCanceled)
        {
            var notCancelRaised = _externalTokenRegistration.Unregister();
            if (!notCancelRaised)
            {
                // may invoking CancellationTokenSource.Cancel so don't call .Dispose.
                return false;
            }
        }

        // if timer is not raised, successful reset so ok to return pool
        if (_cancellationTokenSource.TryReset())
        {
            _calledExternalTokenCancel = false;
            _externalCancellationToken = default;
            _externalTokenRegistration = default;
            _timeout = TimeSpan.Zero;

            _pool.Return(this);
            return true;
        }
        else
        {
            // otherwise, don't reuse.
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            return false;
        }
    }
}
