using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class PooledValueTaskSource<TResponse> : IObjectPoolNode<PooledValueTaskSource<TResponse>>, IValueTaskSource<NatsMsg<TResponse>>
{
    private static readonly Action<object?> CancelAction = SetCancel;
    private PooledValueTaskSource<TResponse>? _next;
    private INatsConnection? _connection;
    private ManualResetValueTaskSourceCore<NatsMsg<TResponse>> _core;
    private ValueTask<InFlightNatsMsg<TResponse>> _source;
    private NatsMsg<TResponse>? _response;
    private ObjectPool? _objectPool;
    private bool _noReturn;

    public PooledValueTaskSource(ObjectPool? pool)
    {
        _objectPool = pool;
        OnCompleted = () =>
        {
            if (_source.IsCompletedSuccessfully)
            {
#pragma warning disable VSTHRD002
                SetResult(_source.Result.ToNatsMsg(_connection));
#pragma warning restore VSTHRD002
            }
            else if (_source.IsFaulted)
            {
                try
                {
#pragma warning disable VSTHRD002
                    _source.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                }
                catch (Exception e)
                {
                    SetException(e);
                }
            }
        };
    }

    public ValueTask<NatsMsg<TResponse>> Run(ValueTask<InFlightNatsMsg<TResponse>> msg, INatsConnection? connection)
    {
        _connection = connection;
        _source = msg;
        msg.GetAwaiter().UnsafeOnCompleted(OnCompleted);
        return AsValueTask();
    }

    public bool IsCanceled { get; private set; }

    public ref PooledValueTaskSource<TResponse>? NextNode => ref _next;

    protected readonly Action OnCompleted;

    public ValueTask<NatsMsg<TResponse>> AsValueTask()
    {
        return new ValueTask<NatsMsg<TResponse>>(this, _core.Version);
    }

    public void SetResult(NatsMsg<TResponse> result)
    {
        _response = result;

        if (IsCanceled)
            return; // already called Canceled, it invoked SetCanceled.

        _core.SetResult(_response.Value);
    }

    public void SetCanceled()
    {
        _noReturn = true;


        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                var ex = new OperationCanceledException();
                state._core.SetException(ex);
            },
            this,
            preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        if (_noReturn)
            return;

        _noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                state.self._core.SetException(state.exception);
            },
            (self: this, exception),
            preferLocal: false);
    }

    NatsMsg<TResponse> IValueTaskSource<NatsMsg<TResponse>>.GetResult(short token)
    {
        try
        {
            return _core.GetResult(token);
        }
        finally
        {
            _core.Reset();
            _response = default!;
            _source = default;
            var p = _objectPool;
            _objectPool = null;

            // canceled object don't return pool to avoid call SetResult/Exception after await
            if (p != null && !_noReturn)
            {
                p.Return(Unsafe.As<PooledValueTaskSource<TResponse>>(this));
            }
        }
    }

    ValueTaskSourceStatus IValueTaskSource<NatsMsg<TResponse>>.GetStatus(short token)
    {
        return _core.GetStatus(token);
    }

    void IValueTaskSource<NatsMsg<TResponse>>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledValueTaskSource<TResponse> RentOrGet(ObjectPool? pool)
    {
        if (pool != null && pool.TryRent<PooledValueTaskSource<TResponse>>(out var self) == true)
        {
            self._objectPool = pool;
        }
        else
        {
            self = new PooledValueTaskSource<TResponse>(pool);
        }

        return self;
    }

    private static void SetCancel(object? state)
    {
        var self = (PooledValueTaskSource<TResponse>)state!;
        self.IsCanceled = true;
        self.SetCanceled();
    }
}
