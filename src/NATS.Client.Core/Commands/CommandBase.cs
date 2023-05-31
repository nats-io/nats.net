#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal abstract class CommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>
    where TSelf : class, IObjectPoolNode<TSelf>
{
    private static readonly Action<object?> CancelAction = SetCancel;

    private TSelf? _next;
    private CancellationTokenRegistration _timerRegistration;
    private CancellationTimer? _timer;

    public bool IsCanceled { get; private set; }

    public ref TSelf? NextNode => ref _next;

    void ICommand.Return(ObjectPool pool)
    {
        _timerRegistration.Dispose(); // wait for cancel callback complete
        _timerRegistration = default;

        // if failed to return timer, maybe invoked timer callback so avoid race condition, does not return command itself to pool.
        if (!IsCanceled && (_timer == null || _timer.TryReturn()))
        {
            _timer = null;
            Reset();
            pool.Return(Unsafe.As<TSelf>(this));
        }
    }

    public abstract void Write(ProtocolWriter writer);

    public void SetCancellationTimer(CancellationTimer timer)
    {
        _timer = timer;
        _timerRegistration = timer.Token.UnsafeRegister(CancelAction, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    protected abstract void Reset();

    private static void SetCancel(object? state)
    {
        var self = (CommandBase<TSelf>)state!;
        self.IsCanceled = true;
    }
}

internal abstract class AsyncCommandBase<TSelf> : ICommand, IAsyncCommand, IObjectPoolNode<TSelf>, IValueTaskSource, IPromise, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    private static readonly Action<object?> CancelAction = SetCancel;

    private TSelf? _next;
    private CancellationTokenRegistration _timerRegistration;
    private CancellationTimer? _timer;

    private ObjectPool? _objectPool;
    private bool _noReturn;

    private ManualResetValueTaskSourceCore<object> _core;

    public bool IsCanceled { get; private set; }

    public ref TSelf? NextNode => ref _next;

    void ICommand.Return(ObjectPool pool)
    {
        // don't return manually, only allows from await.
        // however, set pool on this timing.
        _objectPool = pool;
    }

    public abstract void Write(ProtocolWriter writer);

    public ValueTask AsValueTask()
    {
        return new ValueTask(this, _core.Version);
    }

    public void SetResult()
    {
        // succeed operation, remove canceler
        _timerRegistration.Dispose();
        _timerRegistration = default;

        if (IsCanceled) return; // already called Canceled, it invoked SetCanceled.

        if (_timer != null)
        {
            if (!_timer.TryReturn())
            {
                // cancel is called. don't set result.
                return;
            }

            _timer = null;
        }

        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled()
    {
        if (_noReturn) return;

        _timerRegistration.Dispose();
        _timerRegistration = default;

        _noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                var ex = state._timer != null
                    ? state._timer.GetExceptionWhenCanceled()
                    : new OperationCanceledException();

                state._core.SetException(ex);
            },
            this,
            preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        if (_noReturn) return;

        _timerRegistration.Dispose();
        _timerRegistration = default;

        _noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                state.self._core.SetException(state.exception);
            },
            (self: this, exception),
            preferLocal: false);
    }

    void IValueTaskSource.GetResult(short token)
    {
        try
        {
            _core.GetResult(token);
        }
        finally
        {
            _core.Reset();
            Reset();
            var p = _objectPool;
            _objectPool = null;
            _timer = null;
            _timerRegistration = default;

            // canceled object don't return pool to avoid call SetResult/Exception after await
            if (p != null && !_noReturn)
            {
                p.Return(Unsafe.As<TSelf>(this));
            }
        }
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        return _core.GetStatus(token);
    }

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    void IThreadPoolWorkItem.Execute()
    {
        _core.SetResult(null!);
    }

    public void SetCancellationTimer(CancellationTimer timer)
    {
        _timer = timer;
        _timerRegistration = timer.Token.UnsafeRegister(CancelAction, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    protected abstract void Reset();

    private static void SetCancel(object? state)
    {
        var self = (AsyncCommandBase<TSelf>)state!;
        self.IsCanceled = true;
        self.SetCanceled();
    }
}

internal abstract class AsyncCommandBase<TSelf, TResponse> : ICommand, IAsyncCommand<TResponse>, IObjectPoolNode<TSelf>, IValueTaskSource<TResponse>, IPromise, IPromise<TResponse>, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    private static readonly Action<object?> CancelAction = SetCancel;

    private TSelf? _next;
    private CancellationTokenRegistration _timerRegistration;
    private CancellationTimer? _timer;
    private ManualResetValueTaskSourceCore<TResponse> _core;
    private TResponse? _response;
    private ObjectPool? _objectPool;
    private bool _noReturn;

    public bool IsCanceled { get; private set; }

    public ref TSelf? NextNode => ref _next;

    void ICommand.Return(ObjectPool pool)
    {
        // don't return manually, only allows from await.
        // however, set pool on this timing.
        _objectPool = pool;
    }

    public abstract void Write(ProtocolWriter writer);

    public ValueTask<TResponse> AsValueTask()
    {
        return new ValueTask<TResponse>(this, _core.Version);
    }

    void IPromise.SetResult()
    {
        // called when SocketWriter.Flush, however continuation should run on response received.
    }

    public void SetResult(TResponse result)
    {
        _response = result;

        if (IsCanceled) return; // already called Canceled, it invoked SetCanceled.

        _timerRegistration.Dispose();
        _timerRegistration = default;

        if (_timer != null && _objectPool != null)
        {
            if (!_timer.TryReturn())
            {
                // cancel is called. don't set result.
                return;
            }

            _timer = null;
        }

        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled()
    {
        _noReturn = true;

        _timerRegistration.Dispose();
        _timerRegistration = default;

        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                var ex = state._timer != null
                    ? state._timer.GetExceptionWhenCanceled()
                    : new OperationCanceledException();
                state._core.SetException(ex);
            },
            this,
            preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        if (_noReturn) return;

        _timerRegistration.Dispose();
        _timerRegistration = default;

        _noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                state.self._core.SetException(state.exception);
            },
            (self: this, exception),
            preferLocal: false);
    }

    TResponse IValueTaskSource<TResponse>.GetResult(short token)
    {
        try
        {
            return _core.GetResult(token);
        }
        finally
        {
            _core.Reset();
            _response = default!;
            Reset();
            var p = _objectPool;
            _objectPool = null;
            _timer = null;
            _timerRegistration = default;

            // canceled object don't return pool to avoid call SetResult/Exception after await
            if (p != null && !_noReturn)
            {
                p.Return(Unsafe.As<TSelf>(this));
            }
        }
    }

    ValueTaskSourceStatus IValueTaskSource<TResponse>.GetStatus(short token)
    {
        return _core.GetStatus(token);
    }

    void IValueTaskSource<TResponse>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    void IThreadPoolWorkItem.Execute()
    {
        _core.SetResult(_response!);
    }

    public void SetCancellationTimer(CancellationTimer timer)
    {
        _timer = timer;
        _timerRegistration = timer.Token.UnsafeRegister(CancelAction, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    protected abstract void Reset();

    private static void SetCancel(object? state)
    {
        var self = (AsyncCommandBase<TSelf, TResponse>)state!;
        self.IsCanceled = true;
        self.SetCanceled();
    }
}
