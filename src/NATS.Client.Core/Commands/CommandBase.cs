#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal abstract class CommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>
    where TSelf : class, IObjectPoolNode<TSelf>
{
    private TSelf? _next;

    public ref TSelf? NextNode => ref _next;

    void ICommand.Return(ObjectPool pool)
    {
        Reset();
        pool.Return(Unsafe.As<TSelf>(this));
    }

    public abstract void Write(ProtocolWriter writer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    protected abstract void Reset();
}

internal abstract class AsyncCommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>, IValueTaskSource, IPromise, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    private TSelf? _next;

    private ObjectPool? _objectPool;
    private bool _noReturn;

    private ManualResetValueTaskSourceCore<object> _core;

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
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        _noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                state.self._core.SetException(new OperationCanceledException(state.cancellationToken));
            },
            (self: this, cancellationToken),
            preferLocal: false);
    }

    public void SetException(Exception exception)
    {
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    protected abstract void Reset();
}

internal abstract class AsyncCommandBase<TSelf, TResponse> : ICommand, IObjectPoolNode<TSelf>, IValueTaskSource<TResponse>, IPromise, IPromise<TResponse>, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    private TSelf? _next;

    private ManualResetValueTaskSourceCore<TResponse> _core;
    private TResponse? _response;

    private ObjectPool? _objectPool;

    private bool _noReturn;

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
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        _noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                state.self._core.SetException(new OperationCanceledException(state.cancellationToken));
            },
            (self: this, cancellationToken),
            preferLocal: false);
    }

    public void SetException(Exception exception)
    {
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    protected abstract void Reset();
}
