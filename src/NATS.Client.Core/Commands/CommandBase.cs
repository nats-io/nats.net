#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal abstract class CommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>
    where TSelf : class, IObjectPoolNode<TSelf>
{
    TSelf? next;
    public ref TSelf? NextNode => ref next;

    protected abstract void Reset();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    void ICommand.Return(ObjectPool pool)
    {
        Reset();
        pool.Return(Unsafe.As<TSelf>(this));
    }

    public abstract void Write(ProtocolWriter writer);
}

internal abstract class AsyncCommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>, IValueTaskSource, IPromise, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    TSelf? next;
    public ref TSelf? NextNode => ref next;

    ObjectPool? objectPool;
    bool noReturn;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    ManualResetValueTaskSourceCore<object> core;

    void ICommand.Return(ObjectPool pool)
    {
        // don't return manually, only allows from await.
        // however, set pool on this timing.
        objectPool = pool;
    }

    public abstract void Write(ProtocolWriter writer);

    protected abstract void Reset();

    public ValueTask AsValueTask()
    {
        return new ValueTask(this, core.Version);
    }

    public void SetResult()
    {
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(new OperationCanceledException(state.cancellationToken));
        }, (self: this, cancellationToken), preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(state.exception);
        }, (self: this, exception), preferLocal: false);
    }

    void IValueTaskSource.GetResult(short token)
    {
        try
        {
            core.GetResult(token);
        }
        finally
        {
            core.Reset();
            Reset();
            var p = objectPool;
            objectPool = null;
            if (p != null && !noReturn) // canceled object don't return pool to avoid call SetResult/Exception after await
            {
                p.Return(Unsafe.As<TSelf>(this));
            }
        }
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    void IThreadPoolWorkItem.Execute()
    {
        core.SetResult(null!);
    }
}

internal abstract class AsyncCommandBase<TSelf, TResponse> : ICommand, IObjectPoolNode<TSelf>,  IValueTaskSource<TResponse>, IPromise, IPromise<TResponse>, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    TSelf? next;
    public ref TSelf? NextNode => ref next;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    ManualResetValueTaskSourceCore<TResponse> core;
    TResponse? response;
    ObjectPool? objectPool;
    bool noReturn;

    void ICommand.Return(ObjectPool pool)
    {
        // don't return manually, only allows from await.
        // however, set pool on this timing.
        objectPool = pool;
    }

    public abstract void Write(ProtocolWriter writer);

    protected abstract void Reset();

    public ValueTask<TResponse> AsValueTask()
    {
        return new ValueTask<TResponse>(this, core.Version);
    }

    void IPromise.SetResult()
    {
        // called when SocketWriter.Flush, however continuation should run on response received.
    }

    public void SetResult(TResponse result)
    {
        response = result;
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(new OperationCanceledException(state.cancellationToken));
        }, (self: this, cancellationToken), preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(state.exception);
        }, (self: this, exception), preferLocal: false);
    }

    TResponse IValueTaskSource<TResponse>.GetResult(short token)
    {
        try
        {
            return core.GetResult(token);
        }
        finally
        {
            core.Reset();
            response = default!;
            Reset();
            var p = objectPool;
            objectPool = null;
            if (p != null && !noReturn) // canceled object don't return pool to avoid call SetResult/Exception after await
            {
                p.Return(Unsafe.As<TSelf>(this));
            }
        }
    }

    ValueTaskSourceStatus IValueTaskSource<TResponse>.GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    void IValueTaskSource<TResponse>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    void IThreadPoolWorkItem.Execute()
    {
        core.SetResult(response!);
    }
}
